using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using SentinelProfiles.Core;
namespace SentinelProfiles.Services;

public sealed class HubProfilesIpc : IDisposable
{
    private readonly IFramework framework;
    private readonly ProfileService profiles;
    private readonly InstalledPluginDiscoveryService discovery;
    private readonly ApplicationCoordinator application;
    private readonly DriftDetector drift;
    private readonly Action<Guid> open;
    private readonly ICallGateProvider<string> snapshot;
    private readonly ICallGateProvider<long,object?> changed;
    private readonly ICallGateProvider<string,bool> openProfile;
    private readonly ICallGateProvider<int> health;
    private bool dirty=true,disposed;
    private bool previousBusy;
    private long inventoryRevision=-1,revision;
    private string content="",semantic="";
    public HubProfilesIpc(IDalamudPluginInterface pi,IFramework framework,ProfileService profiles,
        InstalledPluginDiscoveryService discovery,ApplicationCoordinator application,DriftDetector drift,Action<Guid> open)
    {
        this.framework=framework;this.profiles=profiles;this.discovery=discovery;
        this.application=application;this.drift=drift;this.open=open;
        snapshot=pi.GetIpcProvider<string>("SentinelProfiles.Hub.v1.Snapshot");
        changed=pi.GetIpcProvider<long,object?>("SentinelProfiles.Hub.v1.Changed");
        openProfile=pi.GetIpcProvider<string,bool>("SentinelProfiles.Hub.v1.Open");
        health=pi.GetIpcProvider<int>("Sentinel.SentinelProfiles.Health.v1");
        snapshot.RegisterFunc(()=>{
            if(disposed||!framework.IsInFrameworkUpdateThread)return "";
            Tick();return content;
        });
        openProfile.RegisterFunc(id=>{
            if(disposed||!framework.IsInFrameworkUpdateThread||!Guid.TryParseExact(id,"D",out var key)
                ||profiles.Find(key) is null||application.IsBusy)return false;
            open(key);return true;
        });
        health.RegisterFunc(()=>disposed?2:0);
    }
    public void Invalidate()=>dirty=true;
    public void Tick()
    {
        if(disposed)return;
        var busy=application.IsBusy;
        if(!dirty&&inventoryRevision==discovery.Revision&&previousBusy==busy)return;
        dirty=false;inventoryRevision=discovery.Revision;previousBusy=busy;
        var value=HubProfileSnapshot.Create(profiles,drift.Evaluate(profiles.LastAppliedProfile,discovery.Snapshot),discovery.Available,busy,0);
        var next=value.Serialize();
        if(next==semantic)return;
        semantic=next;content=(value with{Revision=++revision}).Serialize();
        // A subscriber cannot prevent the authoritative provider from updating.
        try{changed.SendMessage(revision);}catch{}
    }
    public void Dispose()
    {
        disposed=true;
        snapshot.UnregisterFunc();openProfile.UnregisterFunc();health.UnregisterFunc();
    }
}
