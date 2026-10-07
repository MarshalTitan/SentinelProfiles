namespace SentinelProfiles.UI;

/// <summary>
/// Profiles-specific sizing for its three literal state buttons. Shared shell/settings-row
/// geometry remains owned by SentinelCore.UI; inputs here are measured from the active font.
/// </summary>
public static class ProfileEditorLayout
{
    public static (bool Inline, float ButtonWidth) StateButtons(
        float availableWidth,
        float longestLabelWidth,
        float framePadding,
        float itemSpacing)
    {
        var width = MathF.Max(1f, availableWidth);
        var minimumButtonWidth = longestLabelWidth + 2f * framePadding;
        var inline = width >= 3f * minimumButtonWidth + 2f * itemSpacing;
        return (inline, inline ? (width - 2f * itemSpacing) / 3f : width);
    }
}
