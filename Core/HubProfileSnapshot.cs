using System.Text.Json;
using SentinelProfiles.Models;
namespace SentinelProfiles.Core;

// Additive primitive JSON contract. No plugin entries, configuration, paths or apply action.
public sealed record HubProfileName(string Id, string Name);
public sealed record HubProfileSnapshot(int Schema, long Revision, HubProfileName[] Profiles,
    string? SelectedProfileId, string? LastAppliedProfileId, string Match, bool Applying)
{
    public static HubProfileSnapshot Create(ProfileService service, DriftResult drift, bool available, bool applying, long revision)
        => new(1, revision, service.Profiles.Select(p=>new HubProfileName(p.Id.ToString("D"),p.Name)).ToArray(),
            service.SelectedProfile?.Id.ToString("D"),service.LastAppliedProfile?.Id.ToString("D"),
            available?drift.State.ToString():"Unavailable",applying);
    public string Serialize()=>JsonSerializer.Serialize(this);
}
