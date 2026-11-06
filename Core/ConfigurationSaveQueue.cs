namespace SentinelProfiles.Core;

/// <summary>
/// Preserves the latest in-memory configuration when Dalamud's SQLite-backed
/// config store is temporarily locked, and retries without blocking UI drawing.
/// </summary>
public sealed class ConfigurationSaveQueue(
    Action persist,
    Action<Exception, int>? onDeferred = null,
    Action? onRecovered = null)
{
    private DateTime nextRetryUtc = DateTime.MinValue;

    public bool Pending { get; private set; }
    public int ConsecutiveLocks { get; private set; }

    public void Request(DateTime nowUtc)
    {
        Pending = true;
        RetryIfDue(nowUtc);
    }

    public bool RetryIfDue(DateTime nowUtc, bool force = false)
    {
        if (!Pending || (!force && nowUtc < nextRetryUtc))
            return false;

        try
        {
            persist(); // Reads the latest configuration, including edits made while locked.
            Pending = false;
            nextRetryUtc = DateTime.MinValue;
            var recovered = ConsecutiveLocks > 0;
            ConsecutiveLocks = 0;
            if (recovered)
                onRecovered?.Invoke();
            return true;
        }
        catch (Exception exception) when (IsDatabaseLocked(exception))
        {
            ConsecutiveLocks++;
            var seconds = 1 << Math.Min(ConsecutiveLocks - 1, 3); // 1, 2, 4, then 8 seconds.
            nextRetryUtc = nowUtc.AddSeconds(seconds);
            onDeferred?.Invoke(exception, ConsecutiveLocks);
            return false;
        }
    }

    public static bool IsDatabaseLocked(Exception exception)
    {
        // Do not add a direct SQLite dependency or suppress unrelated write errors.
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().FullName == "SQLite.SQLiteException" &&
                (current.Message.Contains("database is locked", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("database table is locked", StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }
}
