using SentinelProfiles.Models;

namespace SentinelProfiles.Core;

public enum DriftState
{
    NoProfileApplied,
    Matched,
    Drifted,
}

public sealed record DriftResult(
    DriftState State,
    int ManagedPlugins,
    IReadOnlyList<string> MismatchedInternalNames,
    IReadOnlyList<string> MissingInternalNames);

public sealed class DriftDetector
{
    private readonly PluginSafetyPolicy safetyPolicy;

    public DriftDetector(PluginSafetyPolicy safetyPolicy)
    {
        this.safetyPolicy = safetyPolicy;
    }

    public DriftResult Evaluate(PluginProfile? profile, IReadOnlyList<InstalledPluginInfo> installedPlugins)
    {
        if (profile is null)
            return new DriftResult(DriftState.NoProfileApplied, 0, [], []);

        var installed = installedPlugins
            .GroupBy(plugin => plugin.InternalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var mismatched = new List<string>();
        var missing = new List<string>();
        var managed = 0;

        foreach (var (internalName, entry) in profile.PluginStates)
        {
            if (safetyPolicy.IsProtected(internalName) || entry.State == ProfilePluginState.LeaveAlone)
                continue;

            managed++;
            if (!installed.TryGetValue(internalName, out var plugin))
            {
                missing.Add(internalName);
                continue;
            }

            var expectedLoaded = entry.State == ProfilePluginState.Enable;
            if (plugin.IsLoaded != expectedLoaded)
                mismatched.Add(internalName);
        }

        return new DriftResult(
            mismatched.Count == 0 && missing.Count == 0 ? DriftState.Matched : DriftState.Drifted,
            managed,
            mismatched,
            missing);
    }
}
