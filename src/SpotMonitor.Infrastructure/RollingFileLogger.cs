using System.Text;

namespace SpotMonitor.Infrastructure;

public sealed class RollingFileLogger
{
    private static readonly Mutex ProcessGate = new(false, "Local\\SpotMonitor.RollingLog");
    private readonly string _directory;
    private readonly object _gate = new();

    public RollingFileLogger(string directory) { _directory = directory; Directory.CreateDirectory(directory); }

    public void Write(string level, string message)
    {
        var safe = RedactCredentials(message);
        lock (_gate)
        {
            var acquired = false;
            try
            {
                try { acquired = ProcessGate.WaitOne(TimeSpan.FromSeconds(2)); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) return;
                var path = Path.Combine(_directory, $"spotmonitor-{DateTime.UtcNow:yyyyMMdd}.log");
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{level}] {safe}{Environment.NewLine}", Encoding.UTF8);
                foreach (var old in Directory.EnumerateFiles(_directory, "spotmonitor-*.log").OrderByDescending(File.GetLastWriteTimeUtc).Skip(14))
                    File.Delete(old);
            }
            finally { if (acquired) ProcessGate.ReleaseMutex(); }
        }
    }

    private static string RedactCredentials(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value, @"(?i)(rtsp://)[^/@\s]+@", "$1***:***@");
}
