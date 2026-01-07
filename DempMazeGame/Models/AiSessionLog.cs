namespace DemoMazeGame.Models
{
    // Data model for AI session logs - captures all metrics for analysis
    public class AiSessionLog
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double DurationSeconds { get; set; }

        public ModelInfo Model { get; set; } = new();
        public SessionSettings Settings { get; set; } = new();
        public SessionOutcome Outcome { get; set; } = new();
        public SessionMetrics Metrics { get; set; } = new();
        public TokenUsage TokenUsage { get; set; } = new();
        public CostInfo Cost { get; set; } = new();

        public List<MoveRecord> Moves { get; set; } = new();
    }

    public class ModelInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class SessionSettings
    {
        public bool ShowCoordinates { get; set; }
        public bool ShowAsciiMap { get; set; }
        public int DelayBetweenMoves { get; set; }
        public bool DistanceToWall { get; set; }
        public bool ShowGoalCoordinates { get; set; }
        public int MaxRevisitsPerCell { get; set; }
        public int MaxMoves { get; set; }
        public bool ReasoningEnabled { get; set; }
        public string ReasoningEffort { get; set; } = "";
        public int? ReasoningMaxTokens { get; set; }
    }

    public class SessionOutcome
    {
        public bool Won { get; set; }
        public bool StoppedByUser { get; set; }
        public bool ReachedMaxMoves { get; set; }
        public bool TooManyRevisits { get; set; }
        public bool ErrorOccurred { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class SessionMetrics
    {
        public int TotalMoves { get; set; }
        public int SuccessfulMoves { get; set; }
        public int WallCollisions { get; set; }
        public int UniquePositionsVisited { get; set; }
        public int BacktrackCount { get; set; }
    }

    public class TokenUsage
    {
        public int TotalPromptTokens { get; set; }
        public int TotalCompletionTokens { get; set; }
        public int TotalTokens { get; set; }
        public int TotalReasoningTokens { get; set; }
    }

    public class CostInfo
    {
        public decimal TotalCostUsd { get; set; }
        public decimal CostPerMove { get; set; }
        // Now using actual cost from OpenRouter's usage accounting, not estimates
        public bool IsEstimate { get; set; } = false;
    }

    public class MoveRecord
    {
        public int MoveNumber { get; set; }
        public string Direction { get; set; } = "";
        public Position FromPosition { get; set; } = new();
        public Position ToPosition { get; set; } = new();
        public bool WasSuccessful { get; set; }
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int ReasoningTokens { get; set; }
        public decimal CostUsd { get; set; }
        public string? Reasoning { get; set; }
    }

    public class Position
    {
        public int Row { get; set; }
        public int Col { get; set; }
    }
}
