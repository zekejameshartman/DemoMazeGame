using System.Text.Json;

namespace DemoMazeGame.Models
{
    // Represents a single API call to OpenRouter
    public class ApiCallEntry
    {
        public int MoveNumber { get; set; }
        public DateTime Timestamp { get; set; }
        public object? Request { get; set; }  // Stores parsed JSON object instead of string
        public object? Response { get; set; }  // Stores parsed JSON object instead of string
        public int HttpStatusCode { get; set; }
        public double LatencyMs { get; set; }
    }

    // The full API log for a session
    public class ApiSessionLog
    {
        public DateTime StartTime { get; set; }
        public string ModelId { get; set; } = "";
        public string ModelName { get; set; } = "";
        public List<ApiCallEntry> Calls { get; set; } = new();
    }
}

