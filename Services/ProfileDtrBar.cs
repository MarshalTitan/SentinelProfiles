using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using SentinelProfiles.Core;
using SentinelProfiles.Models;

namespace SentinelProfiles.Services;

/// <summary>Owns the optional server-info entry and its lightweight quick-apply menu.</summary>
public sealed class ProfileDtrBar(IDtrBar bar, Func<bool> enabled, Func<bool> loggedIn,
    Func<ProfileBarPresentation> read, Func<IReadOnlyList<PluginProfile>> profiles,
    Func<Guid?> lastAppliedId, Func<bool> isBusy, Func<Guid, bool> apply,
    Action open, IPluginLog log) : IDisposable
{
    private IDtrBarEntry? entry;
    private ProfileBarPresentation? previous;
    private DateTime nextRefresh;
    private bool faulted;
    private bool disposed;
    private bool quickMenuOpen;
    private bool skipOutsideClick;
    private Vector2 menuAnchor;

    public void Update()
    {
        if (disposed) return;
        if (!enabled())
        {
            if (entry is not null) entry.Shown = false;
            quickMenuOpen = false;
            faulted = false;
            return;
        }
        if (!loggedIn())
        {
            if (entry is not null) entry.Shown = false;
            quickMenuOpen = false;
            return;
        }
        if (faulted || DateTime.UtcNow < nextRefresh) return;
        nextRefresh = DateTime.UtcNow.AddMilliseconds(500);
        try
        {
            entry ??= bar.Get("Sentinel Profiles");
            entry.OnClick ??= OnEntryClicked;
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

    private void OnEntryClicked(DtrInteractionEvent interaction)
    {
        quickMenuOpen = !quickMenuOpen;
        skipOutsideClick = quickMenuOpen;
        menuAnchor = interaction.Position;
    }

    public void DrawQuickMenu()
    {
        if (disposed || !enabled() || !loggedIn() || !quickMenuOpen)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var display = ImGui.GetIO().DisplaySize;
        var width = MathF.Min(280f * scale, MathF.Max(1f, display.X - 16f * scale));
        var count = profiles().Count;
        var height = MathF.Min(MathF.Max(140f * scale, display.Y - 16f * scale),
            MathF.Max(150f * scale, 88f * scale + count * ImGui.GetFrameHeightWithSpacing()));
        var x = Math.Clamp(menuAnchor.X - width / 2f, 8f * scale,
            MathF.Max(8f * scale, display.X - width - 8f * scale));
        var y = Math.Clamp(menuAnchor.Y + 16f * scale, 8f * scale,
            MathF.Max(8f * scale, display.Y - height - 8f * scale));

        ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new Vector2(width, height), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f) * scale);
        var visible = ImGui.Begin("Profiles quick switch##SentinelProfiles.DtrMenu",
            ref quickMenuOpen,
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove);
        try
        {
            if (!visible)
                return;

            ImGui.TextDisabled(isBusy() ? "Applying a profile..." : "Choose a profile to apply");
            ImGui.Separator();
            var listHeight = MathF.Max(1f, ImGui.GetContentRegionAvail().Y -
                ImGui.GetFrameHeightWithSpacing() - ImGui.GetStyle().ItemSpacing.Y);
            if (ImGui.BeginChild("##SentinelProfiles.QuickList", new Vector2(0f, listHeight)))
            {
                if (count == 0)
                    ImGui.TextWrapped("No profiles yet. Create one in Sentinel Profiles.");

                ImGui.BeginDisabled(isBusy());
                foreach (var profile in profiles())
                {
                    var selected = lastAppliedId() == profile.Id;
                    var label = profile.Name + (selected ? "  • Last" : "");
                    var padding = ImGui.GetStyle().FramePadding;
                    var widthAvailable = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
                    var textWidth = MathF.Max(1f, widthAvailable - 2f * padding.X);
                    var heightAvailable = MathF.Max(ImGui.GetFrameHeight(),
                        ImGui.CalcTextSize(label, false, textWidth).Y + 2f * padding.Y);
                    var origin = ImGui.GetCursorScreenPos();
                    if (ImGui.Selectable($"##quick-{profile.Id:N}", selected,
                            ImGuiSelectableFlags.None, new Vector2(widthAvailable, heightAvailable))
                        && apply(profile.Id))
                        quickMenuOpen = false;

                    var next = ImGui.GetCursorScreenPos();
                    ImGui.SetCursorScreenPos(origin + padding);
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textWidth);
                    ImGui.TextUnformatted(label);
                    ImGui.PopTextWrapPos();
                    ImGui.SetCursorScreenPos(next);
                }
                ImGui.EndDisabled();
            }
            ImGui.EndChild();
            if (ImGui.Button("Open Profiles", new Vector2(-1f, 0f)))
            {
                quickMenuOpen = false;
                open();
            }

            if (!skipOutsideClick && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                && !ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows))
                quickMenuOpen = false;
            skipOutsideClick = false;
        }
        finally
        {
            ImGui.End();
            ImGui.PopStyleVar();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        quickMenuOpen = false;
        if (entry is null) return;
        entry.OnClick = null;
        entry.Remove();
        entry = null;
    }
}
