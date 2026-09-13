using SentinelProfiles.Models;

namespace SentinelProfiles.Core;

public sealed class ProfileService
{
    private const int MaximumNameLength = 64;

    private readonly ProfileConfigurationData configuration;
    private readonly PluginSafetyPolicy safetyPolicy;
    private readonly Action changed;

    public ProfileService(ProfileConfigurationData configuration, PluginSafetyPolicy safetyPolicy, Action changed)
    {
        this.configuration = configuration;
        this.safetyPolicy = safetyPolicy;
        this.changed = changed;

        if (Normalize())
            changed();
    }

    public IReadOnlyList<PluginProfile> Profiles => configuration.Profiles;

    public PluginProfile? SelectedProfile => Find(configuration.SelectedProfileId);

    public PluginProfile? LastAppliedProfile => Find(configuration.LastAppliedProfileId);

    public PluginProfile? Find(Guid? id)
        => id is null ? null : configuration.Profiles.FirstOrDefault(profile => profile.Id == id.Value);

    public PluginProfile? FindByName(string name)
        => configuration.Profiles.FirstOrDefault(
            profile => profile.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    public void Select(Guid id)
    {
        if (configuration.SelectedProfileId == id || Find(id) is null)
            return;

        configuration.SelectedProfileId = id;
        changed();
    }

    public bool TryCreateBlank(string requestedName, out PluginProfile? profile, out string error)
    {
        if (!TryValidateNewName(requestedName, null, out var name, out error))
        {
            profile = null;
            return false;
        }

        profile = new PluginProfile { Name = name };
        configuration.Profiles.Add(profile);
        configuration.SelectedProfileId = profile.Id;
        changed();
        return true;
    }

    public bool TryCaptureCurrent(
        string requestedName,
        IReadOnlyList<InstalledPluginInfo> installedPlugins,
        out PluginProfile? profile,
        out string error)
    {
        if (!TryCreateBlank(requestedName, out profile, out error) || profile is null)
            return false;

        foreach (var plugin in installedPlugins)
        {
            if (safetyPolicy.IsProtected(plugin.InternalName))
                continue;

            profile.SetState(
                plugin.InternalName,
                plugin.DisplayName,
                plugin.IsLoaded ? ProfilePluginState.Enable : ProfilePluginState.Disable);
        }

        changed();
        return true;
    }

    public PluginProfile Duplicate(PluginProfile source)
    {
        var duplicate = source.Clone(GetUniqueCopyName(source.Name));
        configuration.Profiles.Add(duplicate);
        configuration.SelectedProfileId = duplicate.Id;
        changed();
        return duplicate;
    }

    public bool TryRename(PluginProfile profile, string requestedName, out string error)
    {
        if (!TryValidateNewName(requestedName, profile.Id, out var name, out error))
            return false;

        profile.Name = name;
        changed();
        return true;
    }

    public bool Delete(PluginProfile profile)
    {
        if (!configuration.Profiles.Remove(profile))
            return false;

        if (configuration.LastAppliedProfileId == profile.Id)
            configuration.LastAppliedProfileId = null;

        if (configuration.SelectedProfileId == profile.Id)
            configuration.SelectedProfileId = configuration.Profiles.FirstOrDefault()?.Id;

        changed();
        return true;
    }

    public void SetPluginState(
        PluginProfile profile,
        string internalName,
        string displayName,
        ProfilePluginState state)
    {
        if (safetyPolicy.IsProtected(internalName))
            state = ProfilePluginState.LeaveAlone;

        profile.SetState(internalName, displayName, state);
        changed();
    }

    public void MarkApplied(Guid profileId)
    {
        configuration.LastAppliedProfileId = profileId;
        changed();
    }

    public void RefreshLastKnownDisplayNames(IReadOnlyList<InstalledPluginInfo> installedPlugins)
    {
        var names = installedPlugins
            .GroupBy(plugin => plugin.InternalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().DisplayName, StringComparer.OrdinalIgnoreCase);
        var didChange = false;

        foreach (var profile in configuration.Profiles)
        {
            foreach (var (internalName, entry) in profile.PluginStates)
            {
                if (!names.TryGetValue(internalName, out var displayName)
                    || string.Equals(entry.LastKnownDisplayName, displayName, StringComparison.Ordinal))
                {
                    continue;
                }

                entry.LastKnownDisplayName = displayName;
                didChange = true;
            }
        }

        if (didChange)
            changed();
    }

    private bool Normalize()
    {
        var didChange = false;
        configuration.Profiles ??= [];

        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in configuration.Profiles)
        {
            if (profile.Id == Guid.Empty || !ids.Add(profile.Id))
            {
                profile.Id = Guid.NewGuid();
                ids.Add(profile.Id);
                didChange = true;
            }

            var baseName = string.IsNullOrWhiteSpace(profile.Name) ? "Profile" : profile.Name.Trim();
            if (baseName.Length > MaximumNameLength)
                baseName = baseName[..MaximumNameLength].Trim();

            var uniqueName = MakeUnique(baseName, names);
            if (!string.Equals(profile.Name, uniqueName, StringComparison.Ordinal))
            {
                profile.Name = uniqueName;
                didChange = true;
            }

            names.Add(profile.Name);

            var normalizedEntries = new Dictionary<string, ProfilePluginEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in profile.PluginStates ?? [])
            {
                if (string.IsNullOrWhiteSpace(pair.Key)
                    || pair.Value is null
                    || pair.Value.State is ProfilePluginState.LeaveAlone
                    || !Enum.IsDefined(pair.Value.State)
                    || safetyPolicy.IsProtected(pair.Key))
                {
                    didChange = true;
                    continue;
                }

                var internalName = pair.Key.Trim();
                var displayName = string.IsNullOrWhiteSpace(pair.Value.LastKnownDisplayName)
                    ? internalName
                    : pair.Value.LastKnownDisplayName.Trim();
                normalizedEntries[internalName] = new ProfilePluginEntry
                {
                    State = pair.Value.State,
                    LastKnownDisplayName = displayName,
                };
            }

            if (profile.PluginStates is null
                || profile.PluginStates.Comparer != StringComparer.OrdinalIgnoreCase
                || normalizedEntries.Count != profile.PluginStates.Count)
            {
                didChange = true;
            }

            profile.PluginStates = normalizedEntries;
        }

        if (configuration.Version != ProfileConfigurationData.CurrentSchemaVersion)
        {
            configuration.Version = ProfileConfigurationData.CurrentSchemaVersion;
            didChange = true;
        }

        if (Find(configuration.SelectedProfileId) is null)
        {
            var selected = configuration.Profiles.FirstOrDefault()?.Id;
            if (configuration.SelectedProfileId != selected)
            {
                configuration.SelectedProfileId = selected;
                didChange = true;
            }
        }

        if (configuration.LastAppliedProfileId is not null && Find(configuration.LastAppliedProfileId) is null)
        {
            configuration.LastAppliedProfileId = null;
            didChange = true;
        }

        return didChange;
    }

    private bool TryValidateNewName(string requestedName, Guid? ignoredProfileId, out string name, out string error)
    {
        name = requestedName.Trim();
        if (name.Length == 0)
        {
            error = "Profile name cannot be empty.";
            return false;
        }

        if (name.Length > MaximumNameLength)
        {
            error = $"Profile names can be at most {MaximumNameLength} characters.";
            return false;
        }

        var candidateName = name;
        if (configuration.Profiles.Any(
                profile => profile.Id != ignoredProfileId
                           && profile.Name.Equals(candidateName, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Profile names must be unique, ignoring capitalization.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private string GetUniqueCopyName(string sourceName)
    {
        var names = configuration.Profiles.Select(profile => profile.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return MakeUnique($"{sourceName} Copy", names);
    }

    private static string MakeUnique(string baseName, IReadOnlySet<string> existingNames)
    {
        if (baseName.Length > MaximumNameLength)
            baseName = baseName[..MaximumNameLength].TrimEnd();

        if (!existingNames.Contains(baseName))
            return baseName;

        for (var suffix = 2; ; suffix++)
        {
            var suffixText = $" {suffix}";
            var stemLength = Math.Max(1, MaximumNameLength - suffixText.Length);
            var stem = baseName.Length > stemLength ? baseName[..stemLength].TrimEnd() : baseName;
            var candidate = stem + suffixText;
            if (!existingNames.Contains(candidate))
                return candidate;
        }
    }
}
