namespace SentinelProfiles.Core;

public enum ApplyProblemKind
{
    Missing,
    Protected,
    Unsupported,
    SwitchFailed,
    VerificationFailed,
}

public sealed record ApplyProblem(string InternalName, string DisplayName, ApplyProblemKind Kind, string Reason);

public sealed record ProfileApplyResult(
    Guid ProfileId,
    string ProfileName,
    int Enabled,
    int Disabled,
    int AlreadyCorrect,
    int LeftAlone,
    IReadOnlyList<ApplyProblem> Problems,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc)
{
    public int Missing => Problems.Count(problem => problem.Kind == ApplyProblemKind.Missing);

    public int Unsupported => Problems.Count(problem => problem.Kind is ApplyProblemKind.Unsupported or ApplyProblemKind.Protected);

    public int Failed => Problems.Count(problem => problem.Kind is ApplyProblemKind.SwitchFailed or ApplyProblemKind.VerificationFailed);

    public bool Succeeded => Problems.Count == 0;
}

public sealed record ApplyProgress(
    string ProfileName,
    int CompletedTransitions,
    int TotalTransitions,
    string? CurrentPlugin,
    bool? TargetEnabled);
