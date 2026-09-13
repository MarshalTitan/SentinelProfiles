namespace SentinelProfiles.Core;

public sealed class PluginSafetyPolicy
{
    private readonly HashSet<string> protectedInternalNames;

    public PluginSafetyPolicy(IEnumerable<string>? additionalProtectedInternalNames = null)
    {
        protectedInternalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SentinelProfiles",
        };

        if (additionalProtectedInternalNames is null)
            return;

        foreach (var internalName in additionalProtectedInternalNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            protectedInternalNames.Add(internalName);
    }

    public IReadOnlySet<string> ProtectedInternalNames => protectedInternalNames;

    public bool IsProtected(string internalName) => protectedInternalNames.Contains(internalName);
}
