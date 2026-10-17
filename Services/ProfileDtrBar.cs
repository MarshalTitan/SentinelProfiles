using Dalamud.Game.Gui.Dtr;
using Dalamud.Plugin.Services;
using SentinelProfiles.Core;

namespace SentinelProfiles.Services;

/// <summary>Owns one optional Dalamud server-info entry; never applies or switches profiles.</summary>
public sealed class ProfileDtrBar(IDtrBar bar, Func<bool> enabled, Func<bool> loggedIn,
    Func<ProfileBarPresentation> read, Action open, IPluginLog log) : IDisposable
{
    private IDtrBarEntry? entry;
    private ProfileBarPresentation? previous;
    private DateTime nextRefresh;
    private bool faulted;
    private bool disposed;

    public void Update()
    {
        if (disposed) return;
        if (!enabled())
        {
            if (entry is not null) entry.Shown = false;
            faulted = false;
            return;
        }
        if (!loggedIn())
        {
            if (entry is not null) entry.Shown = false;
            return;
        }
        if (faulted || DateTime.UtcNow < nextRefresh) return;
        nextRefresh = DateTime.UtcNow.AddMilliseconds(500);
        try
        {
            entry ??= bar.Get("Sentinel Profiles");
            entry.OnClick ??= _ => open();
            var current = read();
            if (current != previous)
            {
                entry.Text = current.Text;
                entry.Tooltip = current.Tooltip;
                previous = current;
            }
            entry.Shown = true; // Dalamud's own user-hidden preference remains authoritative.
        }
        catch (Exception exception)
        {
            faulted = true;
            if (entry is not null) entry.Shown = false;
            log.Warning("Profiles server info display stopped ({Type}); toggle it Off/On to retry.", exception.GetType().Name);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (entry is null) return;
        entry.OnClick = null;
        entry.Remove();
        entry = null;
    }
}
