using System.Text;

namespace RTSPView.Infrastructure;

public sealed class RollingFileLogger
{
    private static readonly Mutex ProcessGate = new(false, "Local\\RTSPView.RollingLog");
    private readonly string _directory;
    private readonly int _maxFileBytes;
    private long _dropped;
    private string? _lastError;
    public long DroppedEntries => Interlocked.Read(ref _dropped);
    public string? LastError => Volatile.Read(ref _lastError);

    public RollingFileLogger(string directory, int maxFileBytes = 4 * 1024 * 1024)
    { _directory = directory; _maxFileBytes = Math.Clamp(maxFileBytes, 1024, 16 * 1024 * 1024); }

    public void Write(string level, string message)
    {
        var safe = RedactCredentials(message);
        // A single noisy error cannot evade the per-file cap.
        if (safe.Length > 8192) safe = safe[..8192] + " [truncated]";
        var line = $"{DateTimeOffset.Now:O} [{level}] {safe}{Environment.NewLine}";
        var bytes = Encoding.UTF8.GetBytes(line);
        if (bytes.Length > _maxFileBytes) bytes = Encoding.UTF8.GetBytes(line[..Math.Min(line.Length, _maxFileBytes / 4 - 1)] + "\n");
        var acquired = false;
        try
        {
            try { acquired = ProcessGate.WaitOne(TimeSpan.FromMilliseconds(50)); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) { Drop("Log writer busy; entry dropped."); return; }
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"rtspview-{DateTime.UtcNow:yyyyMMdd}.log");
            if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > _maxFileBytes)
                File.Move(path, Path.Combine(_directory, $"rtspview-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.log"));
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) stream.Write(bytes);
            foreach (var old in Directory.EnumerateFiles(_directory, "rtspview-*.log").OrderByDescending(File.GetLastWriteTimeUtc).Skip(14)) File.Delete(old);
            Volatile.Write(ref _lastError, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Drop("Log storage unavailable (" + error.GetType().Name + "). Check disk space and folder permissions."); }
        finally { if (acquired) ProcessGate.ReleaseMutex(); }
    }

    private void Drop(string error) { Interlocked.Increment(ref _dropped); Volatile.Write(ref _lastError, error); }

    public static string RedactCredentials(string value)
    {
        // Omit complete URLs: paths and query parameters can carry credentials too.
        var safe = System.Text.RegularExpressions.Regex.Replace(value, @"(?i)\b(?:rtsp|https?|ftp)://[^\s""<>]+", "[URL redacted]");
        safe = System.Text.RegularExpressions.Regex.Replace(safe, @"(?i)\b(?:authorization|cookie|set-cookie|password|token|secret|api[_-]?key)\s*[:=].*", "[sensitive field redacted]");
        safe = System.Text.RegularExpressions.Regex.Replace(safe, @"(?i)(?:[A-Z]:\\|\\\\)[^\r\n""<>]+|/(?:home|Users)/[^\s]+", "[path redacted]");
        safe = System.Text.RegularExpressions.Regex.Replace(safe, @"\b[\w.%+-]+@[\w.-]+\.[A-Za-z]{2,}\b", "[email redacted]");
        return safe.Replace('\r', ' ').Replace('\n', ' ');
    }
}
