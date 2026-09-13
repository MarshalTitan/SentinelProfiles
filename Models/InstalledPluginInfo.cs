namespace SentinelProfiles.Models;

public sealed record InstalledPluginInfo(
    string InternalName,
    string DisplayName,
    bool IsLoaded,
    bool SupportsProfiles,
    bool IsOutdated,
    bool IsBanned,
    bool IsOrphaned,
    bool IsDecommissioned,
    bool IsDev)
{
    public string? GetUnsupportedReason()
    {
        if (!SupportsProfiles)
            return "The plugin does not support Dalamud plugin collections.";
        if (IsBanned)
            return "Dalamud has blocked this plugin.";
        if (IsOutdated && !IsDev)
            return "The plugin targets an outdated Dalamud API.";
        if (IsDecommissioned)
            return "The plugin is no longer provided by its repository.";
        if (IsOrphaned)
            return "The plugin's source repository is unavailable; changing it could make it impossible to reload.";

        return null;
    }
}
