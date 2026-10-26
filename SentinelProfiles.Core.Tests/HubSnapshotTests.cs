using SentinelProfiles.Core;
using SentinelProfiles.Models;
namespace SentinelProfiles.Core.Tests;
internal static class HubSnapshotTests
{
    public static Task Projection()
    {
        var config=new ProfileConfigurationData();var policy=new PluginSafetyPolicy();
        var service=new ProfileService(config,policy,()=>{});
        service.TryCreateBlank("Raid",out var raid,out _);service.TryCreateBlank("Hunts",out var hunts,out _);
        if(raid is null||hunts is null)throw new Exception("Fixture creation failed.");
        service.MarkApplied(raid.Id);
        service.SetPluginState(raid,"SensitivePlugin","Private configuration marker",ProfilePluginState.Enable);
        var drift=new DriftDetector(policy).Evaluate(service.LastAppliedProfile,[]);
        var view=HubProfileSnapshot.Create(service,drift,true,true,3);
        if(view.Schema!=1||view.Revision!=3||view.Profiles.Length!=2||view.SelectedProfileId!=hunts.Id.ToString("D")
            ||view.LastAppliedProfileId!=raid.Id.ToString("D")||view.Match!="Drifted"||!view.Applying)throw new Exception("Authoritative projection mismatch.");
        var json=view.Serialize();
        if(json.Contains("SensitivePlugin")||json.Contains("Private configuration marker")||json.Contains("PluginStates"))throw new Exception("Private rules crossed IPC projection.");
        service.TryRename(hunts,"Gathering",out _);
        var updated=HubProfileSnapshot.Create(service,drift,true,false,4);
        if(updated.Profiles.Last().Id!=view.Profiles.Last().Id||updated.Profiles.Last().Name!="Gathering")throw new Exception("Stable ID changed on rename.");
        if(HubProfileSnapshot.Create(service,drift,false,false,5).Match!="Unavailable")throw new Exception("Stale inventory claimed current match.");
        service.Delete(hunts);
        if(HubProfileSnapshot.Create(service,drift,true,false,6).Profiles.Length!=1)throw new Exception("Deleted profile retained.");
        return Task.CompletedTask;
    }
    public static Task EmptyAndMatched()
    {
        var config=new ProfileConfigurationData();var policy=new PluginSafetyPolicy();var service=new ProfileService(config,policy,()=>{});
        var detector=new DriftDetector(policy);
        var view=HubProfileSnapshot.Create(service,detector.Evaluate(null,[]),true,false,0);
        if(view.Profiles.Length!=0||view.SelectedProfileId is not null||view.LastAppliedProfileId is not null||view.Match!="NoProfileApplied")throw new Exception("Empty profile contract mismatch.");
        service.TryCreateBlank("Casual",out var profile,out _);service.MarkApplied(profile!.Id);
        if(HubProfileSnapshot.Create(service,detector.Evaluate(profile,[]),true,false,1).Match!="Matched")throw new Exception("Matched profile not exposed.");
        return Task.CompletedTask;
    }
}
