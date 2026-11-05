using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using SentinelCore.UI;
using SentinelProfiles.Core;
using SentinelProfiles.Models;

namespace SentinelProfiles.Services;

/// <summary>Owns the optional server-info entry and its lightweight quick-apply menu.</summary>
public sealed class ProfileDtrBar(IDtrBar bar, Func<bool> enabled, Func<bool> loggedIn,
    Func<ProfileBarPresentation> read, Func<IReadOnlyList<PluginProfile>> profiles,
    Func<Guid?> lastAppliedId, Func<bool> isBusy, Func<Guid, bool> apply,
    Action open, IPluginLog log) : IDisposable
{
    public Window QuickMenuWindow { get; } = new QuickSwitchWindow(profiles, lastAppliedId, isBusy, apply, open)
    {
        ShowCloseButton = false,
        AllowPinning = false,
        AllowClickthrough = false,
    };

    private QuickSwitchWindow Menu => (QuickSwitchWindow)QuickMenuWindow;
    private IDtrBarEntry? entry;
    private ProfileBarPresentation? previous;
    private DateTime nextRefresh;
    private bool faulted;
    private bool disposed;

    public void Update()
    {
        if (disposed) return;
        // Framework updates continue even when the configuration window is not being drawn.
        Menu.CloseIfIdle(DateTime.UtcNow);
        if (!enabled())
        {
            if (entry is not null) entry.Shown = false;
            Menu.IsOpen = false;
            faulted = false;
            return;
        }
        if (!loggedIn())
        {
            if (entry is not null) entry.Shown = false;
            Menu.IsOpen = false;
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
        if (Menu.IsOpen)
            Menu.IsOpen = false;
        else
            Menu.OpenAt(interaction.Position);
    }

    // Use Dalamud's window host for mouse and controller focus just like the main editor.
    // A standalone ImGui.Begin from UiBuilder.Draw can render over the game while its
    // contents fail to receive clicks in the server-info area.
    private sealed class QuickSwitchWindow(
        Func<IReadOnlyList<PluginProfile>> profiles,
        Func<Guid?> lastAppliedId,
        Func<bool> isBusy,
        Func<Guid, bool> apply,
        Action open) : Window("Profiles quick switch##SentinelProfiles.DtrMenu",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove), IDisposable
    {
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(7);
        private readonly SentinelModernStyleScope style = new();
        private DateTime idleDeadlineUtc;

        public Vector2 Anchor { get; private set; }

        public void OpenAt(Vector2 anchor)
        {
            Anchor = anchor;
            idleDeadlineUtc = DateTime.UtcNow + IdleTimeout;
            IsOpen = true;
            RequestFocus = true;
        }

        public void CloseIfIdle(DateTime nowUtc)
        {
            if (IsOpen && nowUtc >= idleDeadlineUtc)
                IsOpen = false;
        }

        public override void PreDraw()
        {
            var scale = ImGuiHelpers.GlobalScale;
            style.Push(scale);
            var display = ImGui.GetIO().DisplaySize;
            var width = MathF.Min(280f * scale, MathF.Max(1f, display.X - 16f * scale));
            var height = MathF.Min(MathF.Max(1f, display.Y - 16f * scale),
                MathF.Max(94f * scale, 66f * scale + profiles().Count * ImGui.GetFrameHeightWithSpacing()));
            Position = new Vector2(
                Math.Clamp(Anchor.X - width / 2f, 8f * scale,
                    MathF.Max(8f * scale, display.X - width - 8f * scale)),
                Math.Clamp(Anchor.Y + 16f * scale, 8f * scale,
                    MathF.Max(8f * scale, display.Y - height - 8f * scale)));
            PositionCondition = ImGuiCond.Always;
            Size = new Vector2(width, height) / scale;
            SizeCondition = ImGuiCond.Always;
        }

        public override void PostDraw() => style.Pop();

        public override void Draw()
        {
            // Reset only for actual activity inside the picker. A stationary cursor
            // does not keep an abandoned panel on screen indefinitely.
            var io = ImGui.GetIO();
            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows) &&
                (io.MouseDelta.LengthSquared() > 0f || io.MouseWheel != 0f ||
                 ImGui.IsMouseClicked(ImGuiMouseButton.Left)))
                idleDeadlineUtc = DateTime.UtcNow + IdleTimeout;
            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) &&
                (ImGui.IsKeyPressed(ImGuiKey.UpArrow) || ImGui.IsKeyPressed(ImGuiKey.DownArrow) ||
                 ImGui.IsKeyPressed(ImGuiKey.GamepadDpadUp) || ImGui.IsKeyPressed(ImGuiKey.GamepadDpadDown)))
                idleDeadlineUtc = DateTime.UtcNow + IdleTimeout;

            var scale = ImGuiHelpers.GlobalScale;
            var snapshot = profiles().ToArray();
            var listHeight = MathF.Max(1f, ImGui.GetContentRegionAvail().Y -
                38f * scale - ImGui.GetStyle().ItemSpacing.Y);
            if (ImGui.BeginChild("##SentinelProfiles.QuickList", new Vector2(0f, listHeight)))
            {
                if (snapshot.Length == 0)
                    ImGui.TextWrapped("No profiles yet.");

                ImGui.BeginDisabled(isBusy());
                ImGui.PushStyleColor(ImGuiCol.Header, SentinelModernPalette.NavigationSelected);
                try
                {
                    foreach (var profile in snapshot)
                    {
                        var selected = lastAppliedId() == profile.Id;
                        var label = profile.Name;
                        var padding = ImGui.GetStyle().FramePadding;
                        var widthAvailable = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
                        var textWidth = MathF.Max(1f, widthAvailable - 2f * padding.X);
                        var rowHeight = MathF.Max(ImGui.GetFrameHeight(),
                            ImGui.CalcTextSize(label, false, textWidth).Y + 2f * padding.Y);
                        var origin = ImGui.GetCursorScreenPos();
                        if (ImGui.Selectable($"##quick-{profile.Id:N}", selected,
                                ImGuiSelectableFlags.None, new Vector2(widthAvailable, rowHeight)))
                        {
                            if (apply(profile.Id))
                                IsOpen = false;
                        }

                        var next = ImGui.GetCursorScreenPos();
                        ImGui.SetCursorScreenPos(origin + padding);
                        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textWidth);
                        ImGui.TextUnformatted(label);
                        ImGui.PopTextWrapPos();
                        ImGui.SetCursorScreenPos(next);
                    }
                }
                finally
                {
                    ImGui.PopStyleColor();
                    ImGui.EndDisabled();
                }
            }
            ImGui.EndChild();
            if (SentinelModernActionDock.PrimaryButton(
                    "SentinelProfiles.OpenProfiles", "Open Profiles", new Vector2(-1f, 0f), scale))
            {
                IsOpen = false;
                open();
            }
        }

        public void Dispose() => style.Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Menu.IsOpen = false;
        Menu.Dispose();
        if (entry is null) return;
        entry.OnClick = null;
        entry.Remove();
        entry = null;
    }
}
