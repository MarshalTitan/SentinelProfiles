using SentinelProfiles.UI;

internal static class ProfileLayoutTests
{
    public static Task StateControlsFit()
    {
        foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
        foreach (var fontFactor in new[] { 0.85f, 1f, 1.2f })
        {
            var enable = 43f * scale * fontFactor;
            var leaveAlone = 77f * scale * fontFactor;
            var disable = 49f * scale * fontFactor;
            var padding = 4f * scale;
            var gap = 4f * scale;
            var minimumDetailWidth = (680f - 64f - 235f - 40f - 28f - 15f - 30f - 16f) * scale;
            foreach (var available in new[] { minimumDetailWidth, 400f * scale, 800f * scale })
            {
                var layout = ProfileEditorLayout.StateButtons(
                    available, enable, leaveAlone, disable, padding, gap);
                var rowWidth = layout.Inline
                    ? layout.EnableWidth + layout.LeaveAloneWidth + layout.DisableWidth + 2f * gap
                    : layout.EnableWidth;
                Assert(layout.Inline, "All three state controls should stay beside each other at the supported minimum.");
                Assert(rowWidth <= available + 0.01f, "State controls exceed their available width.");
                Assert(layout.EnableWidth >= enable + 2f * padding, "Enable label is clipped.");
                Assert(layout.LeaveAloneWidth >= leaveAlone + 2f * padding, "Leave Alone label is clipped.");
                Assert(layout.DisableWidth >= disable + 2f * padding, "Disable label is clipped.");
            }
        }
        return Task.CompletedTask;
    }

    public static Task StateControlsReflowAtBoundary()
    {
        const float enable = 48f;
        const float leaveAlone = 91f;
        const float disable = 52f;
        const float padding = 6f;
        const float spacing = 9f;
        const float boundary = 245f;
        var below = ProfileEditorLayout.StateButtons(
            boundary - 1f, enable, leaveAlone, disable, padding, spacing);
        Assert(!below.Inline, "Controls should stack only below their actual intrinsic width.");
        Assert(below.EnableWidth == boundary - 1f && below.LeaveAloneWidth == boundary - 1f
               && below.DisableWidth == boundary - 1f, "Stacked controls should fill their row.");
        Assert(ProfileEditorLayout.StateButtons(
            boundary, enable, leaveAlone, disable, padding, spacing).Inline, "Boundary should fit inline.");
        Assert(ProfileEditorLayout.StateButtons(
            boundary + 1f, enable, leaveAlone, disable, padding, spacing).Inline, "Above boundary should fit inline.");
        Assert(ProfileEditorLayout.StateButtons(
            0f, enable, leaveAlone, disable, padding, spacing).EnableWidth > 0f, "Buttons need usable width.");
        return Task.CompletedTask;
    }

    public static Task PluginActionsWrap()
    {
        foreach (var scale in new[] { 1f, 1.5f, 2f })
        foreach (var fontFactor in new[] { 0.85f, 1f, 1.2f })
        {
            var available = 252f * scale;
            var open = 33f * scale * fontFactor;
            var settings = 55f * scale * fontFactor;
            var padding = 8f * scale;
            var gap = 8f * scale;
            var compact = ProfileEditorLayout.PluginActionButtons(
                available, 260f * scale, open, settings, padding, gap);
            Assert(!compact.AlongsideName && compact.ButtonsInline,
                "Long names should wrap above the adjacent Open and Settings controls.");
            Assert(compact.OpenWidth + gap + compact.SettingsWidth <= available,
                "Plugin buttons exceed the supported minimum width.");
            Assert(compact.OpenWidth >= open + 2f * padding &&
                   compact.SettingsWidth >= settings + 2f * padding,
                "Plugin action labels are clipped.");

            var wide = ProfileEditorLayout.PluginActionButtons(
                800f * scale, 260f * scale, open, settings, padding, gap);
            Assert(wide.AlongsideName && wide.ButtonsInline,
                "Plugin name and actions should share a row when space permits.");

            var tiny = ProfileEditorLayout.PluginActionButtons(
                100f * scale, 260f * scale, open, settings, padding, gap);
            Assert(!tiny.AlongsideName && !tiny.ButtonsInline &&
                   tiny.OpenWidth == 100f * scale && tiny.SettingsWidth == 100f * scale,
                "Below the supported minimum, controls should stack and fill their row.");
        }

        return Task.CompletedTask;
    }

    private static void Assert(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }
}
