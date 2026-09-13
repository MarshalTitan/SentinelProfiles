namespace SentinelProfiles.Models;

/// <summary>
/// The action a profile takes for one installed plugin.
/// </summary>
public enum ProfilePluginState
{
    LeaveAlone = 0,
    Enable = 1,
    Disable = 2,
}
