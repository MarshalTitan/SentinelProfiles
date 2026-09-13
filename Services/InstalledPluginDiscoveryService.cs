using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SentinelProfiles.Models;

namespace SentinelProfiles.Services;

public sealed class InstalledPluginDiscoveryService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly object gate = new();

    private IReadOnlyList<InstalledPluginInfo> plugins = [];
    private DateTime refreshAfterUtc = DateTime.MinValue;
    private bool dirty = true;

    public InstalledPluginDiscoveryService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        pluginInterface.ActivePluginsChanged += OnActivePluginsChanged;
        RefreshNow();
    }

    public IReadOnlyList<InstalledPluginInfo> Snapshot
    {
        get
        {
            lock (gate)
                return plugins;
        }
    }

    public void RefreshIfDue()
    {
        lock (gate)
        {
            if (!dirty && DateTime.UtcNow < refreshAfterUtc)
                return;
        }

        RefreshNow();
    }

    public IReadOnlyList<InstalledPluginInfo> RefreshNow()
    {
        try
        {
            var refreshed = pluginInterface.InstalledPlugins
                .Where(plugin => !string.IsNullOrWhiteSpace(plugin.InternalName))
                .Select(plugin => new InstalledPluginInfo(
                    plugin.InternalName,
                    string.IsNullOrWhiteSpace(plugin.Name) ? plugin.InternalName : plugin.Name,
                    plugin.IsLoaded,
                    plugin.Manifest.SupportsProfiles,
                    plugin.IsOutdated,
                    plugin.IsBanned,
                    plugin.IsOrphaned,
                    plugin.IsDecommissioned,
                    plugin.IsDev))
                .OrderBy(plugin => plugin.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(plugin => plugin.InternalName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            lock (gate)
            {
                plugins = refreshed;
                dirty = false;
                refreshAfterUtc = DateTime.UtcNow + RefreshInterval;
            }

            return refreshed;
        }
        catch (Exception exception)
        {
            log.Warning(exception, "Could not refresh the installed Dalamud plugin list.");
            lock (gate)
            {
                dirty = true;
                refreshAfterUtc = DateTime.UtcNow + RefreshInterval;
                return plugins;
            }
        }
    }

    public void Dispose()
    {
        pluginInterface.ActivePluginsChanged -= OnActivePluginsChanged;
    }

    private void OnActivePluginsChanged(IActivePluginsChangedEventArgs _)
    {
        lock (gate)
            dirty = true;
    }
}
