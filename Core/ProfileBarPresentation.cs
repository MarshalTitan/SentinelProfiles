using SentinelProfiles.Models;

namespace SentinelProfiles.Core;

/// <summary>Read-only summary of the last applied profile; editor selection never implies activation.</summary>
public sealed record ProfileBarPresentation(string Text, string Tooltip)
{
    public static ProfileBarPresentation Create(PluginProfile? applied, DriftResult drift,
        bool applying = false, string? applyingName = null, bool applyProblems = false)
    {
        if (applying)
            return new("Profiles: applying", "Applying " + Clean(applyingName) + ". Open Profiles for progress.");
        if (applied is null)
            return new("Profiles: none", "No profile has been applied. Click to open Sentinel Profiles.");
        var name = Clean(applied.Name);
        var shortName = name.Length <= 24 ? name : name[..24] + "…";
        var warning = applyProblems || drift.State != DriftState.Matched;
        var status = applyProblems ? "Last application reported problems; open Profiles for details."
            : drift.State == DriftState.Matched ? "Explicitly managed plugin states currently match."
            : $"Changed: {drift.MismatchedInternalNames.Count}; missing: {drift.MissingInternalNames.Count}.";
        return new("Profiles: " + shortName + (warning ? " !" : ""),
            $"Last applied profile: {name}\n{status}\nSelection/editing does not apply a profile. Click to open Profiles.");
    }

    private static string Clean(string? value) =>
        new((value ?? "").Where(c => !char.IsControl(c)).Take(64).ToArray());
}
