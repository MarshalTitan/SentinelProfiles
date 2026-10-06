namespace SentinelProfiles.Models;

[Serializable]
public class ProfileConfigurationData
{
    public const int CurrentSchemaVersion = 3;

    public int Version { get; set; } = CurrentSchemaVersion;

    public List<PluginProfile> Profiles { get; set; } = [];

    public Guid? SelectedProfileId { get; set; }

    public Guid? LastAppliedProfileId { get; set; }

    public EditorFilter EditorFilter { get; set; } = EditorFilter.AllPlugins;

    public bool ShowInternalNames { get; set; } = true;

    /// <summary>
    /// Persisted <c>SentinelThemeKind</c> value. Schema-one users migrate explicitly
    /// to Classic (0); Sentinel Core normalizes this value again at the UI boundary.
    /// </summary>
    public int Theme { get; set; }

    /// <summary>
    /// Custom Sentinel Modern header state. Classic continues to use ImGui's native window controls.
    /// </summary>
    public bool ModernWindowCollapsed { get; set; }

    /// <summary>
    /// Last expanded Sentinel Modern width in logical pixels.
    /// </summary>
    public float ModernExpandedWidth { get; set; }

    /// <summary>
    /// Last expanded Sentinel Modern height in logical pixels.
    /// </summary>
    public float ModernExpandedHeight { get; set; }
}
