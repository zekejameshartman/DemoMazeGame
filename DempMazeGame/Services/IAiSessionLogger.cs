using DemoMazeGame.Models;

namespace DemoMazeGame.Services
{
    // Interface for AI session logging - creates separate log files per game
    public interface IAiSessionLogger
    {
        void StartSession(string modelId, string modelName, bool showCoordinates, bool showAsciiMap, int delayBetweenMoves);
        void LogMove(int moveNumber, string direction, int fromRow, int fromCol, int toRow, int toCol, bool wasSuccessful, int promptTokens, int completionTokens, decimal costUsd);
        void EndSession(bool won, bool stoppedByUser, bool reachedMaxMoves, bool errorOccurred, string? errorMessage = null);
    }
}
