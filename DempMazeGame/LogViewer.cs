using System.Text.Json;
using DemoMazeGame.Models;
using Spectre.Console;

namespace DemoMazeGame
{
    // Displays session logs in a readable format
    public class LogViewer
    {
        private readonly string _sessionsDir;

        public LogViewer()
        {
            _sessionsDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "logs", "sessions");
        }

        public void ShowLogSelector()
        {
            while (true)
            {
                AnsiConsole.Clear();

                AnsiConsole.Write(
                    new Rule("[bold cyan]Session Log Viewer[/]")
                        .RuleStyle("grey")
                        .Centered());

                AnsiConsole.WriteLine();

                // Get all session log files (not API logs)
                if (!Directory.Exists(_sessionsDir))
                {
                    AnsiConsole.MarkupLine("[yellow]No session logs found. Play some AI games first![/]");
                    AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
                    Console.ReadKey(true);
                    return;
                }

                var logFiles = Directory.GetFiles(_sessionsDir, "*.json")
                    .Where(f => !f.EndsWith("_api.json"))  // Exclude API logs from list
                    .OrderByDescending(f => f)
                    .ToList();

                if (logFiles.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No session logs found. Play some AI games first![/]");
                    AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
                    Console.ReadKey(true);
                    return;
                }

                // Build choices showing filename and outcome
                var choices = new List<string>();
                var fileMap = new Dictionary<string, string>();

                foreach (var file in logFiles.Take(20))  // Show last 20
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    string displayName = GetLogDisplayName(file);
                    choices.Add(displayName);
                    fileMap[displayName] = file;
                }
                choices.Add("[grey]← Back to Main Menu[/]");

                var selection = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[green]Select a session to view:[/]")
                        .PageSize(15)
                        .HighlightStyle(new Style(Color.Cyan1, decoration: Decoration.Bold))
                        .AddChoices(choices));

                if (selection.Contains("Back"))
                {
                    return;
                }

                if (fileMap.TryGetValue(selection, out string? selectedFile))
                {
                    ViewSessionLog(selectedFile);
                }
            }
        }

        private string GetLogDisplayName(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                var session = JsonSerializer.Deserialize<AiSessionLog>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (session != null)
                {
                    string outcome = session.Outcome.Won ? "[green]WON[/]" :
                                    session.Outcome.StoppedByUser ? "[yellow]STOPPED[/]" :
                                    session.Outcome.ReachedMaxMoves ? "[red]MAX_MOVES[/]" :
                                    session.Outcome.TooManyRevisits ? "[red]LOOPS[/]" :
                                    session.Outcome.ErrorOccurred ? "[red]ERROR[/]" : "[grey]?[/]";

                    string time = session.StartTime.ToLocalTime().ToString("MM/dd HH:mm");
                    return $"{time} | {session.Model.Name} | {session.Metrics.TotalMoves} moves | {outcome}";
                }
            }
            catch { }

            return Path.GetFileNameWithoutExtension(filePath);
        }

        private void ViewSessionLog(string sessionFilePath)
        {
            AnsiConsole.Clear();

            try
            {
                // Load session log
                string sessionJson = File.ReadAllText(sessionFilePath);
                var session = JsonSerializer.Deserialize<AiSessionLog>(sessionJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (session == null)
                {
                    AnsiConsole.MarkupLine("[red]Failed to parse session log.[/]");
                    Console.ReadKey(true);
                    return;
                }

                // Try to load paired API log
                string apiLogPath = sessionFilePath.Replace(".json", "_api.json");
                ApiSessionLog? apiLog = null;
                if (File.Exists(apiLogPath))
                {
                    string apiJson = File.ReadAllText(apiLogPath);
                    apiLog = JsonSerializer.Deserialize<ApiSessionLog>(apiJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }

                // Display session summary
                DisplaySessionSummary(session);

                // Ask what to view
                while (true)
                {
                    AnsiConsole.WriteLine();
                    var viewChoice = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[green]What would you like to view?[/]")
                            .HighlightStyle(new Style(Color.Cyan1))
                            .AddChoices(new[]
                            {
                                "View all moves (prompts & responses)",
                                "View specific move",
                                "View raw API log",
                                "[grey]← Back to log list[/]"
                            }));

                    if (viewChoice.Contains("Back"))
                    {
                        break;
                    }
                    else if (viewChoice.Contains("all moves"))
                    {
                        DisplayAllMoves(session, apiLog);
                    }
                    else if (viewChoice.Contains("specific move"))
                    {
                        int moveNum = AnsiConsole.Prompt(
                            new TextPrompt<int>($"[yellow]Enter move number[/] [grey](1-{session.Metrics.TotalMoves})[/]:")
                                .DefaultValue(1)
                                .Validate(m => m >= 1 && m <= session.Metrics.TotalMoves
                                    ? ValidationResult.Success()
                                    : ValidationResult.Error("[red]Invalid move number[/]")));

                        DisplaySingleMove(session, apiLog, moveNum);
                    }
                    else if (viewChoice.Contains("raw API"))
                    {
                        if (apiLog != null)
                        {
                            DisplayRawApiLog(apiLog);
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[yellow]No API log found for this session.[/]");
                            Console.ReadKey(true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error reading log: {ex.Message}[/]");
                Console.ReadKey(true);
            }
        }

        private void DisplaySessionSummary(AiSessionLog session)
        {
            AnsiConsole.Write(
                new Rule($"[bold cyan]{session.Model.Name}[/]")
                    .RuleStyle("grey")
                    .Centered());

            AnsiConsole.WriteLine();

            // Outcome
            string outcome = session.Outcome.Won ? "[bold green]WON[/]" :
                            session.Outcome.StoppedByUser ? "[yellow]Stopped by user[/]" :
                            session.Outcome.ReachedMaxMoves ? "[red]Reached max moves[/]" :
                            session.Outcome.TooManyRevisits ? "[red]Too many revisits (looping)[/]" :
                            session.Outcome.ErrorOccurred ? $"[red]Error: {session.Outcome.ErrorMessage}[/]" : "[grey]Unknown[/]";

            var summaryTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn("[yellow]Metric[/]")
                .AddColumn("[yellow]Value[/]");

            summaryTable.AddRow("Outcome", outcome);
            summaryTable.AddRow("Date", session.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            summaryTable.AddRow("Duration", $"{session.DurationSeconds:F1} seconds");
            summaryTable.AddRow("Total Moves", session.Metrics.TotalMoves.ToString());
            summaryTable.AddRow("Successful Moves", session.Metrics.SuccessfulMoves.ToString());
            summaryTable.AddRow("Wall Collisions", session.Metrics.WallCollisions.ToString());
            summaryTable.AddRow("Unique Positions", session.Metrics.UniquePositionsVisited.ToString());
            summaryTable.AddRow("Backtracks", session.Metrics.BacktrackCount.ToString());
            summaryTable.AddRow("Total Tokens", session.TokenUsage.TotalTokens.ToString("N0"));
            summaryTable.AddRow("Reasoning Tokens", session.TokenUsage.TotalReasoningTokens.ToString("N0"));
            summaryTable.AddRow("Total Cost", $"${session.Cost.TotalCostUsd:F6}");

            AnsiConsole.Write(summaryTable);
        }

        private void DisplayAllMoves(AiSessionLog session, ApiSessionLog? apiLog)
        {
            AnsiConsole.Clear();

            AnsiConsole.Write(
                new Rule("[bold cyan]All Moves - Prompts & Responses[/]")
                    .RuleStyle("grey")
                    .Centered());

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to advance, Q to quit viewing[/]");
            AnsiConsole.WriteLine();

            foreach (var move in session.Moves)
            {
                DisplayMoveDetails(move, apiLog);

                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Q)
                {
                    break;
                }
            }
        }

        private void DisplaySingleMove(AiSessionLog session, ApiSessionLog? apiLog, int moveNumber)
        {
            AnsiConsole.Clear();

            var move = session.Moves.FirstOrDefault(m => m.MoveNumber == moveNumber);
            if (move == null)
            {
                AnsiConsole.MarkupLine("[red]Move not found.[/]");
                Console.ReadKey(true);
                return;
            }

            AnsiConsole.Write(
                new Rule($"[bold cyan]Move {moveNumber}[/]")
                    .RuleStyle("grey")
                    .Centered());

            AnsiConsole.WriteLine();

            DisplayMoveDetails(move, apiLog);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
            Console.ReadKey(true);
        }

        private void DisplayMoveDetails(MoveRecord move, ApiSessionLog? apiLog)
        {
            // Move header
            string success = move.WasSuccessful ? "[green]OK[/]" : "[red]WALL[/]";
            AnsiConsole.Write(new Rule($"[yellow]Move {move.MoveNumber}[/] - {move.Direction} - {success}").RuleStyle("grey").LeftJustified());

            AnsiConsole.MarkupLine($"[grey]From:[/] ({move.FromPosition.Col}, {move.FromPosition.Row}) [grey]To:[/] ({move.ToPosition.Col}, {move.ToPosition.Row})");
            AnsiConsole.MarkupLine($"[grey]Tokens:[/] {move.PromptTokens} prompt + {move.CompletionTokens} completion = {move.PromptTokens + move.CompletionTokens} total");

            if (move.ReasoningTokens > 0)
            {
                AnsiConsole.MarkupLine($"[grey]Reasoning tokens:[/] {move.ReasoningTokens}");
            }

            AnsiConsole.MarkupLine($"[grey]Cost:[/] ${move.CostUsd:F6}");

            // Show reasoning if available
            if (!string.IsNullOrEmpty(move.Reasoning))
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[cyan]── AI Reasoning ──[/]");
                // Preserve line breaks in reasoning
                foreach (var line in move.Reasoning.Split('\n'))
                {
                    AnsiConsole.WriteLine(line);
                }
            }

            // Get API call details for this move
            var apiCall = apiLog?.Calls.FirstOrDefault(c => c.MoveNumber == move.MoveNumber);
            if (apiCall != null)
            {
                // Extract and display the prompt from request
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]── Prompt Sent ──[/]");
                DisplayPromptFromRequest(apiCall.Request);

                // Extract and display the response
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[green]── AI Response ──[/]");
                DisplayContentFromResponse(apiCall.Response);
            }

            AnsiConsole.WriteLine();
        }

        private void DisplayPromptFromRequest(object? request)
        {
            if (request == null) return;

            try
            {
                // Request is stored as JsonElement
                if (request is JsonElement jsonElement)
                {
                    if (jsonElement.TryGetProperty("messages", out JsonElement messages))
                    {
                        foreach (var msg in messages.EnumerateArray())
                        {
                            if (msg.TryGetProperty("role", out JsonElement role) &&
                                msg.TryGetProperty("content", out JsonElement content))
                            {
                                string roleStr = role.GetString() ?? "";
                                string contentStr = content.GetString() ?? "";

                                if (roleStr == "user")
                                {
                                    // Display user prompt with line breaks preserved
                                    foreach (var line in contentStr.Split('\n'))
                                    {
                                        AnsiConsole.WriteLine(line);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                AnsiConsole.MarkupLine("[grey](Could not parse request)[/]");
            }
        }

        private void DisplayContentFromResponse(object? response)
        {
            if (response == null) return;

            try
            {
                if (response is JsonElement jsonElement)
                {
                    if (jsonElement.TryGetProperty("choices", out JsonElement choices))
                    {
                        foreach (var choice in choices.EnumerateArray())
                        {
                            if (choice.TryGetProperty("message", out JsonElement message))
                            {
                                // Display content (reasoning/thinking)
                                if (message.TryGetProperty("content", out JsonElement content))
                                {
                                    string contentStr = content.GetString() ?? "";
                                    if (!string.IsNullOrWhiteSpace(contentStr))
                                    {
                                        // Display with line breaks preserved
                                        foreach (var line in contentStr.Split('\n'))
                                        {
                                            AnsiConsole.WriteLine(line);
                                        }
                                    }
                                }

                                // Display tool calls
                                if (message.TryGetProperty("tool_calls", out JsonElement toolCalls))
                                {
                                    AnsiConsole.WriteLine();
                                    AnsiConsole.MarkupLine("[cyan]Tool call:[/]");
                                    foreach (var toolCall in toolCalls.EnumerateArray())
                                    {
                                        if (toolCall.TryGetProperty("function", out JsonElement function))
                                        {
                                            string funcName = "";
                                            string args = "";

                                            if (function.TryGetProperty("name", out JsonElement name))
                                                funcName = name.GetString() ?? "";
                                            if (function.TryGetProperty("arguments", out JsonElement arguments))
                                                args = arguments.GetString() ?? "";

                                            AnsiConsole.MarkupLine($"  [yellow]{funcName}[/]: {args}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                AnsiConsole.MarkupLine("[grey](Could not parse response)[/]");
            }
        }

        private void DisplayRawApiLog(ApiSessionLog apiLog)
        {
            AnsiConsole.Clear();

            AnsiConsole.Write(
                new Rule("[bold cyan]Raw API Log[/]")
                    .RuleStyle("grey")
                    .Centered());

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[grey]Model:[/] {apiLog.ModelName}");
            AnsiConsole.MarkupLine($"[grey]Total API calls:[/] {apiLog.Calls.Count}");
            AnsiConsole.WriteLine();

            foreach (var call in apiLog.Calls)
            {
                AnsiConsole.Write(new Rule($"[yellow]Move {call.MoveNumber}[/]").RuleStyle("grey").LeftJustified());
                AnsiConsole.MarkupLine($"[grey]Status:[/] {call.HttpStatusCode} [grey]Latency:[/] {call.LatencyMs:F0}ms");
                AnsiConsole.WriteLine();

                // Pretty print JSON
                var options = new JsonSerializerOptions { WriteIndented = true };

                if (call.Request != null)
                {
                    AnsiConsole.MarkupLine("[yellow]Request:[/]");
                    string requestJson = JsonSerializer.Serialize(call.Request, options);
                    AnsiConsole.WriteLine(requestJson);
                }

                AnsiConsole.WriteLine();

                if (call.Response != null)
                {
                    AnsiConsole.MarkupLine("[green]Response:[/]");
                    string responseJson = JsonSerializer.Serialize(call.Response, options);
                    AnsiConsole.WriteLine(responseJson);
                }

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]Press any key for next call, Q to quit...[/]");

                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Q)
                {
                    break;
                }

                AnsiConsole.Clear();
            }
        }
    }
}
