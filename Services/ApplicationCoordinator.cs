using Dalamud.Plugin.Services;
using SentinelProfiles.Core;
using SentinelProfiles.Models;

namespace SentinelProfiles.Services;

public enum ApplyOrigin
{
    UserInterface,
    Command,
}

public sealed class ApplicationCoordinator : IDisposable
{
    private readonly ProfileApplicationEngine engine;
    private readonly ProfileService profiles;
    private readonly IPluginLog log;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object gate = new();

    private Task<ProfileApplyResult>? activeTask;
    private ApplyOrigin activeOrigin;
    private ApplyProgress? progress;
    private string? fatalError;

    public ApplicationCoordinator(
        ProfileApplicationEngine engine,
        ProfileService profiles,
        IPluginLog log)
    {
        this.engine = engine;
        this.profiles = profiles;
        this.log = log;
    }

    public event Action<ProfileApplyResult, ApplyOrigin>? Completed;

    public bool IsBusy
    {
        get
        {
            lock (gate)
                return activeTask is not null;
        }
    }

    public ApplyProgress? Progress
    {
        get
        {
            lock (gate)
                return progress;
        }
    }

    public ProfileApplyResult? LastResult { get; private set; }

    public string? FatalError
    {
        get
        {
            lock (gate)
                return fatalError;
        }
    }

    public bool Start(PluginProfile profile, ApplyOrigin origin)
    {
        lock (gate)
        {
            if (activeTask is not null)
                return false;

            var snapshot = Snapshot(profile);
            progress = new ApplyProgress(snapshot.Name, 0, 0, null, null);
            fatalError = null;
            activeOrigin = origin;
            log.Information(
                "Starting profile apply: {ProfileName} ({ProfileId}); {ManagedCount} explicitly managed plugins.",
                snapshot.Name,
                snapshot.Id,
                snapshot.PluginStates.Count);
            activeTask = Task.Run(
                () => engine.ApplyAsync(snapshot, ReportProgress, lifetime.Token),
                lifetime.Token);
            return true;
        }
    }

    public void Update()
    {
        Task<ProfileApplyResult>? completedTask;
        ApplyOrigin origin;
        lock (gate)
        {
            if (activeTask is null || !activeTask.IsCompleted)
                return;

            completedTask = activeTask;
            activeTask = null;
            progress = null;
            origin = activeOrigin;
        }

        try
        {
            var result = completedTask.GetAwaiter().GetResult();
            LastResult = result;
            profiles.MarkApplied(result.ProfileId);
            LogResult(result);
            Completed?.Invoke(result, origin);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            log.Information("Sentinel Profiles apply operation was cancelled during shutdown.");
        }
        catch (Exception exception)
        {
            lock (gate)
                fatalError = $"Profile application stopped unexpectedly: {exception.Message}";
            log.Error(exception, "Profile application stopped unexpectedly.");
        }
    }

    public void Dispose()
    {
        Task<ProfileApplyResult>? task;
        lock (gate)
            task = activeTask;

        lifetime.Cancel();
        if (task is not null)
        {
            _ = task.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        lifetime.Dispose();
    }

    private void ReportProgress(ApplyProgress value)
    {
        lock (gate)
            progress = value;
    }

    private void LogResult(ProfileApplyResult result)
    {
        log.Information(
            "Profile apply completed: {ProfileName}; enabled={Enabled}, disabled={Disabled}, alreadyCorrect={AlreadyCorrect}, leftAlone={LeftAlone}, problems={Problems}.",
            result.ProfileName,
            result.Enabled,
            result.Disabled,
            result.AlreadyCorrect,
            result.LeftAlone,
            result.Problems.Count);

        foreach (var problem in result.Problems)
        {
            log.Warning(
                "Profile apply problem for {InternalName} ({Kind}): {Reason}",
                problem.InternalName,
                problem.Kind,
                problem.Reason);
        }
    }

    private static PluginProfile Snapshot(PluginProfile profile)
    {
        var snapshot = profile.Clone(profile.Name);
        snapshot.Id = profile.Id;
        return snapshot;
    }
}
