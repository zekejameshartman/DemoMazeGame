namespace DemoMazeGame.Services
{
    // Interface for application-wide logging
    public interface IAppLogger
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception? exception = null);
    }
}
