namespace DemoMazeGame.Models
{
    // Result from an AI move request, including token usage for logging
    public class AiMoveResult
    {
        public string Direction { get; set; } = "";
        public bool IsError { get; set; }
        public string? ErrorMessage { get; set; }
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
        // Actual cost in USD as reported by OpenRouter's usage accounting
        public decimal ActualCostUsd { get; set; }

        // The AI's full response text (for displaying reasoning)
        public string RawResponse { get; set; } = "";

        // Reasoning tokens (if available from the model)
        public string? Reasoning { get; set; }
        public int ReasoningTokens { get; set; }

        // Raw API data for detailed logging
        public string RequestJson { get; set; } = "";
        public string ResponseJson { get; set; } = "";
        public int HttpStatusCode { get; set; }
        public double LatencyMs { get; set; }
    }
}
