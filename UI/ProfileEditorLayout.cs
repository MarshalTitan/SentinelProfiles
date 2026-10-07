namespace SentinelProfiles.UI;

/// <summary>
/// Profiles-specific sizing for its three literal state buttons. Shared shell/settings-row
/// geometry remains owned by SentinelCore.UI; inputs here are measured from the active font.
/// </summary>
public static class ProfileEditorLayout
{
    public static float MinimumInlineWidth(
        float enableLabelWidth,
        float leaveAloneLabelWidth,
        float disableLabelWidth,
        float framePadding,
        float itemSpacing)
        => enableLabelWidth + leaveAloneLabelWidth + disableLabelWidth
           + 6f * framePadding + 2f * itemSpacing;

    public static (bool Inline, float EnableWidth, float LeaveAloneWidth, float DisableWidth) StateButtons(
        float availableWidth,
        float enableLabelWidth,
        float leaveAloneLabelWidth,
        float disableLabelWidth,
        float framePadding,
        float itemSpacing)
    {
        var width = MathF.Max(1f, availableWidth);
        var inline = width >= MinimumInlineWidth(
            enableLabelWidth, leaveAloneLabelWidth, disableLabelWidth, framePadding, itemSpacing);
        if (!inline)
            return (false, width, width, width);

        var extra = (width - MinimumInlineWidth(
            enableLabelWidth, leaveAloneLabelWidth, disableLabelWidth, framePadding, itemSpacing)) / 3f;
        return (true,
            enableLabelWidth + 2f * framePadding + extra,
            leaveAloneLabelWidth + 2f * framePadding + extra,
            disableLabelWidth + 2f * framePadding + extra);
    }
}
