using SentinelProfiles.Models;

namespace SentinelProfiles.Core;

public interface IPluginRuntime
{
    Task<IReadOnlyList<InstalledPluginInfo>> GetInstalledPluginsAsync(CancellationToken cancellationToken);

    Task<PluginSwitchResult> SetStateAsync(
        string internalName,
        bool enabled,
        CancellationToken cancellationToken);
}

public sealed record PluginSwitchResult(bool Success, string Reason)
{
    public static PluginSwitchResult Succeeded() => new(true, string.Empty);

    public static PluginSwitchResult Failed(string reason) => new(false, reason);
}
