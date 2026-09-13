using SentinelProfiles.Models;

namespace SentinelProfiles.Core;

public sealed class ProfileApplicationEngine
{
    private readonly IPluginRuntime runtime;
    private readonly PluginSafetyPolicy safetyPolicy;

    public ProfileApplicationEngine(IPluginRuntime runtime, PluginSafetyPolicy safetyPolicy)
    {
        this.runtime = runtime;
        this.safetyPolicy = safetyPolicy;
    }

    public async Task<ProfileApplyResult> ApplyAsync(
        PluginProfile profile,
        Action<ApplyProgress>? reportProgress,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var initial = Index(await runtime.GetInstalledPluginsAsync(cancellationToken).ConfigureAwait(false));
        var transitions = new List<Transition>();
        var alreadyCorrect = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var preflightProblems = new Dictionary<string, ApplyProblem>(StringComparer.OrdinalIgnoreCase);

        foreach (var (internalName, entry) in profile.PluginStates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var displayName = string.IsNullOrWhiteSpace(entry.LastKnownDisplayName)
                ? internalName
                : entry.LastKnownDisplayName;

            if (safetyPolicy.IsProtected(internalName))
            {
                preflightProblems[internalName] = new ApplyProblem(
                    internalName,
                    displayName,
                    ApplyProblemKind.Protected,
                    "Sentinel Profiles protects this plugin from profile switching.");
                continue;
            }

            if (!initial.TryGetValue(internalName, out var plugin))
            {
                preflightProblems[internalName] = new ApplyProblem(
                    internalName,
                    displayName,
                    ApplyProblemKind.Missing,
                    "Plugin is not currently installed. The saved rule was preserved.");
                continue;
            }

            var targetEnabled = entry.State == ProfilePluginState.Enable;
            if (plugin.IsLoaded == targetEnabled)
            {
                alreadyCorrect.Add(internalName);
                continue;
            }

            var unsupportedReason = plugin.GetUnsupportedReason();
            if (unsupportedReason is not null)
            {
                preflightProblems[internalName] = new ApplyProblem(
                    internalName,
                    plugin.DisplayName,
                    ApplyProblemKind.Unsupported,
                    unsupportedReason);
                continue;
            }

            transitions.Add(new Transition(internalName, plugin.DisplayName, targetEnabled));
        }

        transitions = transitions
            .OrderBy(transition => transition.TargetEnabled)
            .ThenBy(transition => transition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var leftAlone = initial.Values.Count(
            plugin => !safetyPolicy.IsProtected(plugin.InternalName)
                      && !profile.PluginStates.ContainsKey(plugin.InternalName));
        var outcomes = new Dictionary<string, PluginSwitchResult>(StringComparer.OrdinalIgnoreCase);
        var completed = 0;

        reportProgress?.Invoke(new ApplyProgress(profile.Name, completed, transitions.Count, null, null));

        foreach (var transition in transitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reportProgress?.Invoke(new ApplyProgress(
                profile.Name,
                completed,
                transitions.Count,
                transition.DisplayName,
                transition.TargetEnabled));

            try
            {
                outcomes[transition.InternalName] = await runtime.SetStateAsync(
                    transition.InternalName,
                    transition.TargetEnabled,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                outcomes[transition.InternalName] = PluginSwitchResult.Failed(
                    $"Switching backend threw {exception.GetType().Name}: {exception.Message}");
            }

            completed++;
            reportProgress?.Invoke(new ApplyProgress(profile.Name, completed, transitions.Count, null, null));
        }

        var final = Index(await runtime.GetInstalledPluginsAsync(cancellationToken).ConfigureAwait(false));
        var problems = new Dictionary<string, ApplyProblem>(preflightProblems, StringComparer.OrdinalIgnoreCase);
        var enabled = 0;
        var disabled = 0;
        var verifiedAlreadyCorrect = 0;

        foreach (var internalName in alreadyCorrect)
        {
            var entry = profile.PluginStates[internalName];
            if (final.TryGetValue(internalName, out var plugin)
                && plugin.IsLoaded == (entry.State == ProfilePluginState.Enable))
            {
                verifiedAlreadyCorrect++;
            }
            else
            {
                problems[internalName] = CreateVerificationProblem(internalName, entry, final);
            }
        }

        foreach (var transition in transitions)
        {
            var outcome = outcomes[transition.InternalName];
            if (outcome.Success
                && final.TryGetValue(transition.InternalName, out var plugin)
                && plugin.IsLoaded == transition.TargetEnabled)
            {
                if (transition.TargetEnabled)
                    enabled++;
                else
                    disabled++;

                continue;
            }

            var reason = outcome.Success
                ? "Final verification did not match the requested state."
                : outcome.Reason;
            problems[transition.InternalName] = new ApplyProblem(
                transition.InternalName,
                transition.DisplayName,
                outcome.Success ? ApplyProblemKind.VerificationFailed : ApplyProblemKind.SwitchFailed,
                reason);
        }

        return new ProfileApplyResult(
            profile.Id,
            profile.Name,
            enabled,
            disabled,
            verifiedAlreadyCorrect,
            leftAlone,
            problems.Values.OrderBy(problem => problem.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray(),
            startedAt,
            DateTime.UtcNow);
    }

    private static Dictionary<string, InstalledPluginInfo> Index(IReadOnlyList<InstalledPluginInfo> plugins)
        => plugins
            .GroupBy(plugin => plugin.InternalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(plugin => plugin.IsLoaded).First(),
                StringComparer.OrdinalIgnoreCase);

    private static ApplyProblem CreateVerificationProblem(
        string internalName,
        ProfilePluginEntry entry,
        IReadOnlyDictionary<string, InstalledPluginInfo> final)
    {
        var displayName = string.IsNullOrWhiteSpace(entry.LastKnownDisplayName)
            ? internalName
            : entry.LastKnownDisplayName;

        return final.ContainsKey(internalName)
            ? new ApplyProblem(
                internalName,
                displayName,
                ApplyProblemKind.VerificationFailed,
                "Plugin state changed while the profile was being applied.")
            : new ApplyProblem(
                internalName,
                displayName,
                ApplyProblemKind.Missing,
                "Plugin was removed while the profile was being applied. The saved rule was preserved.");
    }

    private sealed record Transition(string InternalName, string DisplayName, bool TargetEnabled);
}
