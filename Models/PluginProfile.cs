namespace SentinelProfiles.Models;

[Serializable]
public sealed class PluginProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Sparse map keyed by stable Dalamud InternalName. Leave Alone entries are omitted.
    /// </summary>
    public Dictionary<string, ProfilePluginEntry> PluginStates { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public ProfilePluginState GetState(string internalName)
        => PluginStates.TryGetValue(internalName, out var entry)
            ? entry.State
            : ProfilePluginState.LeaveAlone;

    public void SetState(string internalName, string displayName, ProfilePluginState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(internalName);

        if (state == ProfilePluginState.LeaveAlone)
        {
            PluginStates.Remove(internalName);
            return;
        }

        PluginStates[internalName] = new ProfilePluginEntry
        {
            State = state,
            LastKnownDisplayName = string.IsNullOrWhiteSpace(displayName) ? internalName : displayName,
        };
    }

    public PluginProfile Clone(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        PluginStates = PluginStates.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase),
    };
}
