namespace SentinelProfiles.Models;

[Serializable]
public class ProfileConfigurationData
{
    public const int CurrentSchemaVersion = 1;

    public int Version { get; set; } = CurrentSchemaVersion;

    public List<PluginProfile> Profiles { get; set; } = [];

    public Guid? SelectedProfileId { get; set; }

    public Guid? LastAppliedProfileId { get; set; }

    public EditorFilter EditorFilter { get; set; } = EditorFilter.AllPlugins;

    public bool ShowInternalNames { get; set; } = true;
}
