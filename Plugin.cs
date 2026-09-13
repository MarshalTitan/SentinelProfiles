using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SentinelProfiles.Core;
using SentinelProfiles.Models;
using SentinelProfiles.Services;
using SentinelProfiles.UI;

namespace SentinelProfiles;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/sprofiles";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windows = new("SentinelProfiles");
    private readonly PluginSafetyPolicy safetyPolicy;
    private readonly InstalledPluginDiscoveryService discovery;
    private readonly NativeCommandPluginRuntime runtime;
    private readonly ProfileApplicationEngine applicationEngine;
    private readonly ApplicationCoordinator applicationCoordinator;
    private readonly DriftDetector driftDetector;
    private readonly MainWindow mainWindow;
    private DateTime nextDisplayNameRefreshUtc = DateTime.MinValue;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        safetyPolicy = new PluginSafetyPolicy();
        Profiles = new ProfileService(Configuration, safetyPolicy, SaveConfiguration);
        discovery = new InstalledPluginDiscoveryService(PluginInterface, Log);
        runtime = new NativeCommandPluginRuntime(PluginInterface, CommandManager, Framework, Log, discovery);
        applicationEngine = new ProfileApplicationEngine(runtime, safetyPolicy);
        applicationCoordinator = new ApplicationCoordinator(applicationEngine, Profiles, Log);
        driftDetector = new DriftDetector(safetyPolicy);
        mainWindow = new MainWindow(
            Configuration,
            Profiles,
            safetyPolicy,
            discovery,
            applicationCoordinator,
            driftDetector,
            ApplyProfile,
            ReapplyLastProfile,
            SaveConfiguration);

        windows.AddWindow(mainWindow);
        applicationCoordinator.Completed += OnApplyCompleted;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Sentinel Profiles. Options: apply <profile name>, reapply, list",
        });

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi += OpenMainWindow;
        Framework.Update += OnFrameworkUpdate;

        Log.Information(
            "Sentinel Profiles loaded with {ProfileCount} saved profiles. Switching backend: Dalamud native temporary plugin commands.",
            Profiles.Profiles.Count);
    }

    public Configuration Configuration { get; }

    public ProfileService Profiles { get; }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenMainWindow;
        CommandManager.RemoveHandler(CommandName);
        applicationCoordinator.Completed -= OnApplyCompleted;
        windows.RemoveAllWindows();
        applicationCoordinator.Dispose();
        runtime.Dispose();
        discovery.Dispose();
    }

    public void SaveConfiguration() => PluginInterface.SavePluginConfig(Configuration);

    private void OnFrameworkUpdate(IFramework _)
    {
        discovery.RefreshIfDue();
        if (DateTime.UtcNow >= nextDisplayNameRefreshUtc)
        {
            nextDisplayNameRefreshUtc = DateTime.UtcNow.AddSeconds(2);
            Profiles.RefreshLastKnownDisplayNames(discovery.Snapshot);
        }
        applicationCoordinator.Update();
    }

    private bool ApplyProfile(PluginProfile profile, ApplyOrigin origin)
    {
        if (applicationCoordinator.Start(profile, origin))
            return true;

        if (origin == ApplyOrigin.Command)
            ChatGui.PrintError("[Sentinel Profiles] A profile is already being applied.");
        return false;
    }

    private bool ReapplyLastProfile(ApplyOrigin origin)
    {
        var profile = Profiles.LastAppliedProfile;
        if (profile is not null)
            return ApplyProfile(profile, origin);

        if (origin == ApplyOrigin.Command)
            ChatGui.PrintError("[Sentinel Profiles] No profile has been applied yet.");
        return false;
    }

    private void OnApplyCompleted(ProfileApplyResult result, ApplyOrigin origin)
    {
        if (origin != ApplyOrigin.Command)
            return;

        var headline = result.Succeeded
            ? $"[Sentinel Profiles] {result.ProfileName} applied successfully."
            : $"[Sentinel Profiles] {result.ProfileName} applied with {result.Problems.Count} problem(s).";
        ChatGui.Print(headline);
        ChatGui.Print(
            $"[Sentinel Profiles] {result.Enabled} enabled, {result.Disabled} disabled, "
            + $"{result.AlreadyCorrect} already correct, {result.LeftAlone} left alone.");

        foreach (var problem in result.Problems)
            ChatGui.PrintError($"[Sentinel Profiles] {problem.DisplayName}: {problem.Reason}");
    }

    private void OnCommand(string _, string arguments)
    {
        var trimmed = arguments.Trim();
        if (trimmed.Length == 0)
        {
            ToggleMainWindow();
            return;
        }

        if (trimmed.Equals("reapply", StringComparison.OrdinalIgnoreCase))
        {
            ReapplyLastProfile(ApplyOrigin.Command);
            return;
        }

        if (trimmed.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            var names = Profiles.Profiles.Select(profile => profile.Name).ToArray();
            ChatGui.Print(names.Length == 0
                ? "[Sentinel Profiles] No profiles have been created."
                : $"[Sentinel Profiles] Profiles: {string.Join(", ", names)}");
            return;
        }

        if (trimmed.StartsWith("apply ", StringComparison.OrdinalIgnoreCase))
        {
            var requestedName = Unquote(trimmed[6..].Trim());
            var profile = Profiles.FindByName(requestedName);
            if (profile is null)
            {
                ChatGui.PrintError($"[Sentinel Profiles] Profile '{requestedName}' was not found.");
                return;
            }

            ApplyProfile(profile, ApplyOrigin.Command);
            return;
        }

        if (trimmed.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            ChatGui.Print("[Sentinel Profiles] /sprofiles | apply <profile name> | reapply | list");
            return;
        }

        ChatGui.PrintError("[Sentinel Profiles] Unknown option. Use /sprofiles help.");
    }

    private void ToggleMainWindow() => mainWindow.IsOpen = !mainWindow.IsOpen;

    private void OpenMainWindow() => mainWindow.IsOpen = true;

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        return value;
    }
}
