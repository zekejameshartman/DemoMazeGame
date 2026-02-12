using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DemoMazeGame.Analytics
{
    public class AnalyticsServer
    {
        private readonly int _port;

        public AnalyticsServer(int port = 5178)
        {
            _port = port;
        }

        public async Task RunUntilKeypressAsync()
        {
            string baseUrl = $"http://127.0.0.1:{_port}";

            string contentRoot = FindContentRoot();
            string sessionsDir = ResolveSessionsDir(contentRoot);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = contentRoot
            });

            builder.Logging.ClearProviders();

            builder.WebHost.UseUrls(baseUrl);

            var app = builder.Build();

            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapGet("/api/sessions", () =>
            {
                var results = new List<SessionSummary>();

                if (!Directory.Exists(sessionsDir))
                {
                    return Results.Ok(results);
                }

                foreach (var file in Directory.EnumerateFiles(sessionsDir, "*.json")
                             .Where(f => !f.EndsWith("_api.json", StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(f => f))
                {
                    try
                    {
                        using var stream = File.OpenRead(file);
                        using var doc = JsonDocument.Parse(stream);
                        var root = doc.RootElement;

                        string sessionId = GetString(root, "sessionId") ?? Path.GetFileNameWithoutExtension(file);
                        string modelName = GetString(root, "model", "name") ?? "Unknown";
                        string modelId = GetString(root, "model", "id") ?? "";

                        var settings = GetObj(root, "settings");
                        var outcome = GetObj(root, "outcome");
                        var metrics = GetObj(root, "metrics");
                        var tokenUsage = GetObj(root, "tokenUsage");
                        var cost = GetObj(root, "cost");

                        bool won = GetBool(outcome, "won");
                        bool stopped = GetBool(outcome, "stoppedByUser");
                        bool reachedMaxMoves = GetBool(outcome, "reachedMaxMoves");
                        bool tooManyRevisits = GetBool(outcome, "tooManyRevisits");
                        bool error = GetBool(outcome, "errorOccurred");

                        results.Add(new SessionSummary
                        {
                            SessionId = sessionId,
                            FileName = Path.GetFileName(file),
                            StartTime = GetDateTime(root, "startTime"),
                            EndTime = GetNullableDateTime(root, "endTime"),
                            DurationSeconds = GetDouble(root, "durationSeconds"),
                            ModelName = modelName,
                            ModelId = modelId,

                            ShowGoalCoordinates = GetNullableBool(settings, "showGoalCoordinates"),
                            Breadcrumbs = GetBool(settings, "breadcrumbs"),
                            DistanceToWall = GetBool(settings, "distanceToWall"),
                            ReasoningEnabled = GetBool(settings, "reasoningEnabled"),

                            Won = won,
                            StoppedByUser = stopped,
                            ReachedMaxMoves = reachedMaxMoves,
                            TooManyRevisits = tooManyRevisits,
                            ErrorOccurred = error,

                            TotalMoves = GetInt(metrics, "totalMoves"),
                            WallCollisions = GetInt(metrics, "wallCollisions"),
                            Backtracks = GetInt(metrics, "backtrackCount"),
                            UniquePositionsVisited = GetInt(metrics, "uniquePositionsVisited"),

                            TotalTokens = GetInt(tokenUsage, "totalTokens"),
                            TotalCostUsd = GetDecimal(cost, "totalCostUsd"),

                            OutcomeType = ComputeOutcomeType(won, stopped, reachedMaxMoves, tooManyRevisits, error)
                        });
                    }
                    catch
                    {
                        // Ignore unreadable files; keep the endpoint resilient.
                    }
                }

                return Results.Ok(results);
            });

            app.MapGet("/api/sessions/{sessionId}", (string sessionId) =>
            {
                if (!Directory.Exists(sessionsDir))
                {
                    return Results.NotFound();
                }

                foreach (var file in Directory.EnumerateFiles(sessionsDir, "*.json")
                             .Where(f => !f.EndsWith("_api.json", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        string? id = GetString(root, "sessionId");
                        if (string.Equals(id, sessionId, StringComparison.OrdinalIgnoreCase))
                        {
                            return Results.Text(json, "application/json");
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                return Results.NotFound();
            });

            try
            {
                await app.StartAsync();

                AnsiConsole.Clear();
                AnsiConsole.Write(
                    new Rule("[bold cyan]Analytics Dashboard[/]")
                        .RuleStyle("grey")
                        .Centered());

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[grey]Sessions dir:[/] [cyan]{sessionsDir}[/]");
                AnsiConsole.MarkupLine($"[grey]Open:[/] [link={baseUrl}][cyan]{baseUrl}[/][/] ");
                AnsiConsole.MarkupLine("[grey]Press any key to stop the dashboard and return to the menu...[/]");
                Console.ReadKey(true);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to start dashboard:[/] {ex.Message}");
                AnsiConsole.MarkupLine("[grey]Press any key to return to the menu...[/]");
                Console.ReadKey(true);
            }
            finally
            {
                try
                {
                    await app.StopAsync();
                }
                catch
                {
                    // ignore
                }

                await app.DisposeAsync();
            }
        }

        private static string ComputeOutcomeType(bool won, bool stopped, bool reachedMaxMoves, bool tooManyRevisits, bool error)
        {
            if (won) return "won";
            if (stopped) return "stopped";
            if (reachedMaxMoves) return "max_moves";
            if (tooManyRevisits) return "loops";
            if (error) return "error";
            return "unknown";
        }

        private static string FindContentRoot()
        {
            // Prefer current working directory (expected: DempMazeGame project dir).
            string cwd = Directory.GetCurrentDirectory();
            if (Directory.Exists(Path.Combine(cwd, "logs")) || Directory.Exists(Path.Combine(cwd, "wwwroot")))
            {
                return cwd;
            }

            // Fallback to project-ish location when running from bin/...
            string fromBaseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            return fromBaseDir;
        }

        private static string ResolveSessionsDir(string contentRoot)
        {
            string direct = Path.Combine(contentRoot, "logs", "sessions");
            if (Directory.Exists(direct))
            {
                return direct;
            }

            // Repo-root run fallback
            string nested = Path.Combine(contentRoot, "DempMazeGame", "logs", "sessions");
            if (Directory.Exists(nested))
            {
                return nested;
            }

            return direct;
        }

        private static JsonElement? GetObj(JsonElement root, string prop)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(prop, out var child) && child.ValueKind == JsonValueKind.Object)
            {
                return child;
            }
            return null;
        }

        private static string? GetString(JsonElement root, string prop)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
            return null;
        }

        private static string? GetString(JsonElement root, string prop1, string prop2)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(prop1, out var el1) && el1.ValueKind == JsonValueKind.Object)
            {
                return GetString(el1, prop2);
            }
            return null;
        }

        private static bool GetBool(JsonElement? obj, string prop)
        {
            if (obj.HasValue && obj.Value.ValueKind == JsonValueKind.Object && obj.Value.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.True)
            {
                return true;
            }
            return false;
        }

        private static bool? GetNullableBool(JsonElement? obj, string prop)
        {
            if (!obj.HasValue || obj.Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!obj.Value.TryGetProperty(prop, out var el))
            {
                return null;
            }

            if (el.ValueKind == JsonValueKind.True) return true;
            if (el.ValueKind == JsonValueKind.False) return false;
            return null;
        }

        private static int GetInt(JsonElement? obj, string prop)
        {
            if (obj.HasValue && obj.Value.ValueKind == JsonValueKind.Object && obj.Value.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number)
            {
                if (el.TryGetInt32(out int i)) return i;
            }
            return 0;
        }

        private static double GetDouble(JsonElement root, string prop)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number)
            {
                if (el.TryGetDouble(out double d)) return d;
            }
            return 0;
        }

        private static decimal GetDecimal(JsonElement? obj, string prop)
        {
            if (obj.HasValue && obj.Value.ValueKind == JsonValueKind.Object && obj.Value.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number)
            {
                if (el.TryGetDecimal(out decimal d)) return d;
            }
            return 0;
        }

        private static DateTime GetDateTime(JsonElement root, string prop)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(prop, out var el))
            {
                if (el.ValueKind == JsonValueKind.String && DateTime.TryParse(el.GetString(), out var dt))
                {
                    return dt;
                }
            }
            return DateTime.MinValue;
        }

        private static DateTime? GetNullableDateTime(JsonElement root, string prop)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(prop, out var el))
            {
                if (el.ValueKind == JsonValueKind.String && DateTime.TryParse(el.GetString(), out var dt))
                {
                    return dt;
                }
            }
            return null;
        }
    }

    public class SessionSummary
    {
        public string SessionId { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double DurationSeconds { get; set; }

        public string ModelId { get; set; } = "";
        public string ModelName { get; set; } = "";

        public bool? ShowGoalCoordinates { get; set; }
        public bool Breadcrumbs { get; set; }
        public bool DistanceToWall { get; set; }
        public bool ReasoningEnabled { get; set; }

        public bool Won { get; set; }
        public bool StoppedByUser { get; set; }
        public bool ReachedMaxMoves { get; set; }
        public bool TooManyRevisits { get; set; }
        public bool ErrorOccurred { get; set; }
        public string OutcomeType { get; set; } = "unknown";

        public int TotalMoves { get; set; }
        public int WallCollisions { get; set; }
        public int Backtracks { get; set; }
        public int UniquePositionsVisited { get; set; }

        public int TotalTokens { get; set; }
        public decimal TotalCostUsd { get; set; }
    }
}
