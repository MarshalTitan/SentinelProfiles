using System.Text.Json;
using SentinelProfiles.Core;
using SentinelProfiles.Models;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Blank profiles are sparse and names are case-insensitively unique", TestBlankAndNames),
    ("Capture creates a full snapshot except protected plugins", TestCapture),
    ("Duplicate is independent and deletion clears last applied", TestDuplicateAndDelete),
    ("Configuration data round-trips and normalizes", TestSerialization),
    ("Apply orders disables before enables and leaves unmanaged plugins alone", TestApplyOrdering),
    ("Apply preserves missing entries and continues after failures", TestPartialFailure),
    ("Reapply verifies an already-correct profile", TestReapply),
    ("Missing rules reconnect when a plugin returns", TestMissingReconnect),
    ("Unsupported plugins fail safely without blocking others", TestUnsupported),
    ("Self-protection blocks unsafe rules", TestSelfProtection),
    ("Drift ignores Leave Alone and detects managed changes", TestDrift),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.Error.WriteLine($"FAIL {test.Name}: {exception}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} core test(s) failed.");
    return 1;
}

Console.WriteLine($"All {tests.Length} core tests passed.");
return 0;

static Task TestBlankAndNames()
{
    var data = new ProfileConfigurationData();
    var service = new ProfileService(data, new PluginSafetyPolicy(), () => { });
    Assert(service.TryCreateBlank("Raid", out var raid, out _), "Raid should be created.");
    Assert(raid is not null && raid.PluginStates.Count == 0, "Blank profile should contain no rules.");
    Assert(!service.TryCreateBlank("raid", out _, out var error), "Case-only duplicate should be rejected.");
    Assert(error.Contains("unique", StringComparison.OrdinalIgnoreCase), "Duplicate error should explain uniqueness.");
    Assert(service.TryRename(raid!, "RAID", out _), "Renaming the same profile with different casing should be allowed.");
    return Task.CompletedTask;
}

static Task TestCapture()
{
    var data = new ProfileConfigurationData();
    var service = new ProfileService(data, new PluginSafetyPolicy(), () => { });
    var plugins = new[]
    {
        Plugin("One", true),
        Plugin("Two", false),
        Plugin("SentinelProfiles", true),
    };

    Assert(service.TryCaptureCurrent("Current", plugins, out var captured, out _), "Capture should succeed.");
    Assert(captured!.GetState("One") == ProfilePluginState.Enable, "Loaded plugin should be captured as Enable.");
    Assert(captured.GetState("Two") == ProfilePluginState.Disable, "Unloaded plugin should be captured as Disable.");
    Assert(captured.GetState("SentinelProfiles") == ProfilePluginState.LeaveAlone, "Protected plugin should not be captured.");
    return Task.CompletedTask;
}

static Task TestDuplicateAndDelete()
{
    var data = new ProfileConfigurationData();
    var service = new ProfileService(data, new PluginSafetyPolicy(), () => { });
    service.TryCreateBlank("Hunts", out var hunts, out _);
    service.SetPluginState(hunts!, "Example", "Example", ProfilePluginState.Enable);
    service.MarkApplied(hunts!.Id);

    var copy = service.Duplicate(hunts);
    Assert(copy.Id != hunts.Id, "Duplicate must get a new stable ID.");
    service.SetPluginState(copy, "Example", "Example", ProfilePluginState.Disable);
    Assert(hunts.GetState("Example") == ProfilePluginState.Enable, "Duplicate must be independent.");
    Assert(service.Delete(hunts), "Delete should succeed.");
    Assert(service.LastAppliedProfile is null, "Deleting last-applied profile should clear the pointer.");
    return Task.CompletedTask;
}

static Task TestSerialization()
{
    var data = new ProfileConfigurationData();
    var service = new ProfileService(data, new PluginSafetyPolicy(), () => { });
    service.TryCreateBlank("PvP", out var profile, out _);
    service.SetPluginState(profile!, "SomePlugin", "Some Plugin", ProfilePluginState.Disable);
    service.MarkApplied(profile!.Id);

    var json = JsonSerializer.Serialize(data);
    var restored = JsonSerializer.Deserialize<ProfileConfigurationData>(json)
                   ?? throw new InvalidOperationException("Deserialization returned null.");
    var normalized = new ProfileService(restored, new PluginSafetyPolicy(), () => { });
    Assert(normalized.LastAppliedProfile?.Name == "PvP", "Last-applied ID should round-trip.");
    Assert(normalized.LastAppliedProfile?.GetState("someplugin") == ProfilePluginState.Disable, "InternalName map should normalize to case-insensitive lookup.");
    return Task.CompletedTask;
}

static async Task TestApplyOrdering()
{
    var runtime = new FakeRuntime(
        Plugin("NeedsEnable", false),
        Plugin("NeedsDisable", true),
        Plugin("Untouched", true));
    var profile = new PluginProfile { Name = "Raid" };
    profile.SetState("NeedsEnable", "Needs Enable", ProfilePluginState.Enable);
    profile.SetState("NeedsDisable", "Needs Disable", ProfilePluginState.Disable);

    var result = await new ProfileApplicationEngine(runtime, new PluginSafetyPolicy())
        .ApplyAsync(profile, null, CancellationToken.None);

    Assert(runtime.Calls.SequenceEqual(new[] { "NeedsDisable:False", "NeedsEnable:True" }), "Disables must run before enables.");
    Assert(result.Disabled == 1 && result.Enabled == 1, "Both transitions should be counted.");
    Assert(result.LeftAlone == 1, "Unmanaged plugin should be left alone.");
    Assert(result.Succeeded, "Apply should succeed.");
}

static async Task TestPartialFailure()
{
    var runtime = new FakeRuntime(Plugin("Broken", true), Plugin("Working", true));
    runtime.Fail.Add("Broken");
    var profile = new PluginProfile { Name = "Casual" };
    profile.SetState("Broken", "Broken", ProfilePluginState.Disable);
    profile.SetState("Working", "Working", ProfilePluginState.Disable);
    profile.SetState("Missing", "Missing Plugin", ProfilePluginState.Enable);

    var result = await new ProfileApplicationEngine(runtime, new PluginSafetyPolicy())
        .ApplyAsync(profile, null, CancellationToken.None);

    Assert(runtime.Calls.Contains("Working:False"), "Failure must not stop unrelated transitions.");
    Assert(result.Disabled == 1, "Working transition should still succeed.");
    Assert(result.Problems.Any(problem => problem.InternalName == "Broken" && problem.Kind == ApplyProblemKind.SwitchFailed), "Failure should be reported.");
    Assert(result.Problems.Any(problem => problem.InternalName == "Missing" && problem.Kind == ApplyProblemKind.Missing), "Missing rule should be reported.");
    Assert(profile.GetState("Missing") == ProfilePluginState.Enable, "Missing entry must remain preserved.");
}

static async Task TestSelfProtection()
{
    var runtime = new FakeRuntime(Plugin("SentinelProfiles", true));
    var profile = new PluginProfile { Name = "Unsafe" };
    profile.SetState("SentinelProfiles", "Sentinel Profiles", ProfilePluginState.Disable);

    var result = await new ProfileApplicationEngine(runtime, new PluginSafetyPolicy())
        .ApplyAsync(profile, null, CancellationToken.None);

    Assert(runtime.Calls.Count == 0, "Protected plugin must never be switched.");
    Assert(result.Problems.Single().Kind == ApplyProblemKind.Protected, "Protected rule should be diagnosed.");
}

static async Task TestReapply()
{
    var runtime = new FakeRuntime(Plugin("Managed", false));
    var profile = new PluginProfile { Name = "Raid" };
    profile.SetState("Managed", "Managed", ProfilePluginState.Enable);
    var engine = new ProfileApplicationEngine(runtime, new PluginSafetyPolicy());

    var first = await engine.ApplyAsync(profile, null, CancellationToken.None);
    var second = await engine.ApplyAsync(profile, null, CancellationToken.None);

    Assert(first.Enabled == 1, "First apply should enable the plugin.");
    Assert(second.Enabled == 0 && second.AlreadyCorrect == 1, "Reapply should verify without another transition.");
    Assert(runtime.Calls.Count == 1, "Reapply must avoid unnecessary commands.");
}

static async Task TestMissingReconnect()
{
    var runtime = new FakeRuntime();
    var profile = new PluginProfile { Name = "Gathering" };
    profile.SetState("Returning", "Returning Plugin", ProfilePluginState.Enable);
    var engine = new ProfileApplicationEngine(runtime, new PluginSafetyPolicy());

    var missing = await engine.ApplyAsync(profile, null, CancellationToken.None);
    Assert(missing.Missing == 1, "Absent plugin should be reported missing.");

    runtime.Install(Plugin("Returning", false));
    var reconnected = await engine.ApplyAsync(profile, null, CancellationToken.None);
    Assert(reconnected.Enabled == 1 && reconnected.Succeeded, "Reinstalled plugin should reconnect by InternalName.");
}

static async Task TestUnsupported()
{
    var unsupported = Plugin("Unsupported", true) with { SupportsProfiles = false };
    var runtime = new FakeRuntime(unsupported, Plugin("Working", true));
    var profile = new PluginProfile { Name = "Crafting" };
    profile.SetState("Unsupported", "Unsupported", ProfilePluginState.Disable);
    profile.SetState("Working", "Working", ProfilePluginState.Disable);

    var result = await new ProfileApplicationEngine(runtime, new PluginSafetyPolicy())
        .ApplyAsync(profile, null, CancellationToken.None);

    Assert(!runtime.Calls.Any(call => call.StartsWith("Unsupported", StringComparison.Ordinal)), "Unsupported plugin should not be switched.");
    Assert(runtime.Calls.Contains("Working:False"), "Unsupported plugin must not block a supported transition.");
    Assert(result.Unsupported == 1 && result.Disabled == 1, "Result should separate unsupported and successful changes.");
}

static Task TestDrift()
{
    var profile = new PluginProfile { Name = "Questing" };
    profile.SetState("Managed", "Managed", ProfilePluginState.Enable);
    var detector = new DriftDetector(new PluginSafetyPolicy());
    var installed = new[] { Plugin("Managed", true), Plugin("Ignored", false) };

    Assert(detector.Evaluate(profile, installed).State == DriftState.Matched, "Matching managed state should match.");
    installed = new[] { Plugin("Managed", false), Plugin("Ignored", true) };
    Assert(detector.Evaluate(profile, installed).State == DriftState.Drifted, "Managed state change should drift.");
    profile.SetState("Managed", "Managed", ProfilePluginState.LeaveAlone);
    Assert(detector.Evaluate(profile, installed).State == DriftState.Matched, "Leave Alone should never drift.");
    return Task.CompletedTask;
}

static InstalledPluginInfo Plugin(string name, bool loaded)
    => new(name, name, loaded, true, false, false, false, false, false);

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class FakeRuntime(params InstalledPluginInfo[] installed) : IPluginRuntime
{
    private readonly Dictionary<string, InstalledPluginInfo> plugins = installed.ToDictionary(
        plugin => plugin.InternalName,
        StringComparer.OrdinalIgnoreCase);

    public List<string> Calls { get; } = [];

    public HashSet<string> Fail { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Install(InstalledPluginInfo plugin) => plugins[plugin.InternalName] = plugin;

    public Task<IReadOnlyList<InstalledPluginInfo>> GetInstalledPluginsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<InstalledPluginInfo>>(plugins.Values.ToArray());
    }

    public Task<PluginSwitchResult> SetStateAsync(string internalName, bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add($"{internalName}:{enabled}");
        if (Fail.Contains(internalName))
            return Task.FromResult(PluginSwitchResult.Failed("Simulated failure."));

        plugins[internalName] = plugins[internalName] with { IsLoaded = enabled };
        return Task.FromResult(PluginSwitchResult.Succeeded());
    }
}
