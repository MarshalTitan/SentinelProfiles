using System.Collections.Concurrent;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SentinelProfiles.Core;
using SentinelProfiles.Models;

namespace SentinelProfiles.Services;

/// <summary>
/// Isolates all interaction with Dalamud's plugin switching surface.
/// It intentionally uses the public temporary plugin commands so native
/// collection definitions remain untouched and no internal reflection is needed.
/// </summary>
public sealed class NativeCommandPluginRuntime : IPluginRuntime, IDisposable
{
    private const string EnableCommand = "/xlenableplugintemp";
    private const string DisableCommand = "/xldisableplugintemp";
    private static readonly TimeSpan TransitionTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly InstalledPluginDiscoveryService discovery;
    private readonly ConcurrentDictionary<string, long> eventGenerations =
        new(StringComparer.OrdinalIgnoreCase);

    public NativeCommandPluginRuntime(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IPluginLog log,
        InstalledPluginDiscoveryService discovery)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.framework = framework;
        this.log = log;
        this.discovery = discovery;
        pluginInterface.ActivePluginsChanged += OnActivePluginsChanged;
    }

    public async Task<IReadOnlyList<InstalledPluginInfo>> GetInstalledPluginsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await framework.RunOnFrameworkThread(discovery.RefreshNow).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return snapshot;
    }

    public async Task<PluginSwitchResult> SetStateAsync(
        string internalName,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (internalName.IndexOfAny(['"', '\r', '\n']) >= 0)
            return PluginSwitchResult.Failed("Plugin InternalName contains characters that cannot be passed safely to Dalamud's command parser.");

        cancellationToken.ThrowIfCancellationRequested();
        var command = enabled ? EnableCommand : DisableCommand;
        var generationBefore = eventGenerations.GetValueOrDefault(internalName);

        var dispatch = await framework.RunOnFrameworkThread(() =>
        {
            var current = discovery.RefreshNow()
                .FirstOrDefault(plugin => plugin.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase));
            if (current is null)
                return new DispatchResult(false, "Plugin is no longer installed.");

            if (current.IsLoaded == enabled)
                return new DispatchResult(true, null, true);

            if (!commandManager.Commands.ContainsKey(command))
            {
                return new DispatchResult(
                    false,
                    $"Dalamud's native temporary plugin command {command} is unavailable. Switching is disabled safely.");
            }

            var handled = commandManager.ProcessCommand($"{command} \"{internalName}\"");
            return handled
                ? new DispatchResult(true, null)
                : new DispatchResult(false, $"Dalamud did not accept {command}.");
        }).ConfigureAwait(false);

        if (!dispatch.Accepted)
        {
            var failureReason = dispatch.FailureReason ?? "Dalamud rejected the plugin command.";
            log.Error(
                "Plugin transition command rejected for {InternalName}: {Reason}",
                internalName,
                failureReason);
            return PluginSwitchResult.Failed(failureReason);
        }

        if (dispatch.AlreadyCorrect)
            return PluginSwitchResult.Succeeded();

        log.Information(
            "Requested {Action} for plugin {InternalName} through Dalamud's temporary plugin command.",
            enabled ? "enable" : "disable",
            internalName);

        var deadline = DateTime.UtcNow + TransitionTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            var snapshot = await GetInstalledPluginsAsync(cancellationToken).ConfigureAwait(false);
            var current = snapshot.FirstOrDefault(
                plugin => plugin.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase));
            if (current is null)
                return PluginSwitchResult.Failed("Plugin was removed before Dalamud confirmed the transition.");

            var generationNow = eventGenerations.GetValueOrDefault(internalName);
            if (generationNow > generationBefore && current.IsLoaded == enabled)
                return PluginSwitchResult.Succeeded();
        }

        var finalSnapshot = await GetInstalledPluginsAsync(cancellationToken).ConfigureAwait(false);
        var final = finalSnapshot.FirstOrDefault(
            plugin => plugin.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase));
        var state = final is null ? "missing" : final.IsLoaded ? "enabled" : "disabled";
        var reason = $"Timed out waiting for Dalamud to confirm the transition (current public state: {state}). "
                     + "The plugin may be busy, may have failed internally, or may belong to multiple native plugin collections; check /xllog.";
        log.Error("Plugin transition timed out for {InternalName}: {Reason}", internalName, reason);
        return PluginSwitchResult.Failed(reason);
    }

    public void Dispose()
    {
        pluginInterface.ActivePluginsChanged -= OnActivePluginsChanged;
    }

    private void OnActivePluginsChanged(IActivePluginsChangedEventArgs args)
    {
        foreach (var internalName in args.AffectedInternalNames)
            eventGenerations.AddOrUpdate(internalName, 1, (_, value) => value + 1);
    }

    private sealed record DispatchResult(bool Accepted, string? FailureReason, bool AlreadyCorrect = false);
}
