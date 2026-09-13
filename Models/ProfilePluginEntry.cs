namespace SentinelProfiles.Models;

[Serializable]
public sealed class ProfilePluginEntry
{
    public ProfilePluginState State { get; set; }

    public string LastKnownDisplayName { get; set; } = string.Empty;

    public ProfilePluginEntry Clone() => new()
    {
        State = State,
        LastKnownDisplayName = LastKnownDisplayName,
    };
}
