namespace DemoMazeGame.Services
{
    // Logs application events to a text file
    public class AppLogger : IAppLogger
    {
        private readonly string _logFilePath;
        private readonly object _lock = new();

        public AppLogger()
        {
            // Log file in logs folder relative to the executable
            string logsDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "logs");
            Directory.CreateDirectory(logsDir);
            _logFilePath = Path.Combine(logsDir, "app.log");
        }

        public void LogInfo(string message)
        {
            WriteLog("INFO", message);
        }

        public void LogWarning(string message)
        {
            WriteLog("WARN", message);
        }

        public void LogError(string message, Exception? exception = null)
        {
            string fullMessage = exception != null
                ? $"{message} | Exception: {exception.GetType().Name}: {exception.Message}"
                : message;
            WriteLog("ERROR", fullMessage);
        }

        private void WriteLog(string level, string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string logLine = $"[{timestamp}] [{level}] {message}";

            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
                }
                catch
                {
                    // Silently fail if we can't write to the log file
                }
            }
        }
    }
}
