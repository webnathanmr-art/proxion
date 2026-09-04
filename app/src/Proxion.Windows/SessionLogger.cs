namespace Proxion.Windows;

/// <summary>Appends timestamped lines to one log file for the lifetime of a session.</summary>
public sealed class SessionLogger
{
    public string LogFilePath { get; }

    public SessionLogger(string logFilePath)
    {
        LogFilePath = logFilePath;
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}][{level}] {message}";
        try
        {
            File.AppendAllLines(LogFilePath, new[] { line });
        }
        catch (IOException)
        {
            // Logging is best-effort; never let a full disk or locked file take the app down.
        }
    }
}
