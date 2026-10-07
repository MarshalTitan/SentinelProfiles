using SentinelProfiles.UI;

internal static class ProfileLayoutTests
{
    public static Task StateControlsFit()
    {
        // Exercise the width budgets of the 680px Modern window after its rail/sidebar,
        // page/card padding, scrollbar, selection column, and table cell padding.
        // Vary fonts independently of UI scale: text metrics, not a fixed 64px button, decide.
        foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
        foreach (var fontFactor in new[] { 0.85f, 1f, 1.2f })
        {
            var label = 77f * scale * fontFactor;
            var padding = 4f * scale;
            var gap = 8f * scale;
            var minimumDetailWidth = (680f - 64f - 235f - 40f - 28f - 15f - 30f - 16f) * scale;
            foreach (var available in new[] { minimumDetailWidth, 400f * scale, 800f * scale })
            {
                var layout = ProfileEditorLayout.StateButtons(available, label, padding, gap);
                var rowWidth = layout.Inline ? 3f * layout.ButtonWidth + 2f * gap : layout.ButtonWidth;
                Assert(rowWidth <= available + 0.01f, "A state row must stay inside its column.");
                Assert(layout.ButtonWidth >= label + 2f * padding,
                    "Every state label must fit its button at the supported minimum.");
                Assert(layout.ButtonWidth > 0f, "Buttons must remain reachable.");
            }
        }
        return Task.CompletedTask;
    }

    public static Task StateControlsReflowAtBoundary()
    {
        const float label = 91f;
        const float padding = 6f;
        const float spacing = 9f;
        const float boundary = 327f;
        Assert(!ProfileEditorLayout.StateButtons(boundary - 1f, label, padding, spacing).Inline,
            "One pixel below the measured fit boundary must stack.");
        Assert(ProfileEditorLayout.StateButtons(boundary, label, padding, spacing).Inline,
            "Exactly fitting state controls may share a row.");
        Assert(ProfileEditorLayout.StateButtons(boundary + 1f, label, padding, spacing).Inline,
            "Wider layouts should retain the segmented row.");
        Assert(ProfileEditorLayout.StateButtons(0f, label, padding, spacing).ButtonWidth > 0f,
            "A transient empty region must not create invalid ImGui button dimensions.");
        return Task.CompletedTask;
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
