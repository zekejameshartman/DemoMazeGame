using DemoMazeGame.Models;
using DemoMazeGame.Services;
using Spectre.Console;

namespace DemoMazeGame
{
    // This class contains the main game logic
    // It handles both human players and AI players
    public class Game
    {
        private readonly IAppLogger _logger;
        private readonly IAiSessionLogger _sessionLogger;

        public Game(IAppLogger logger, IAiSessionLogger sessionLogger)
        {
            _logger = logger;
            _sessionLogger = sessionLogger;
        }

        // The maze map - 1 = wall, 0 = open path, 2 = exit
        // This is a more interesting maze for testing AI spatial reasoning
        private int[,] map =
        {
            {1,1,1,1,1,1,1,1,1,1,1,1},
            {1,0,0,0,1,0,0,0,0,0,0,1},
            {1,0,1,0,1,0,1,1,1,1,0,1},
            {1,0,1,0,0,0,0,0,0,1,0,1},
            {1,0,1,1,1,1,1,1,0,1,0,1},
            {1,0,0,0,0,0,0,0,0,1,0,1},
            {1,1,1,1,1,1,1,1,0,1,0,1},
            {1,0,0,0,0,0,0,0,0,1,0,1},
            {1,0,1,1,1,1,1,1,1,1,0,1},
            {1,0,0,0,0,0,0,0,0,0,0,2},
            {1,1,1,1,1,1,1,1,1,1,1,1},
        };

        // Player position
        private int playerRow = 1;
        private int playerCol = 1;

        // Game statistics
        private int moveCount = 0;
        private bool gameWon = false;
        private decimal runningCost = 0m;  // Running total of API costs
        private int totalTokens = 0;       // Running total of tokens used

        // Move history for AI prompting
        private List<GameMoveRecord> moveHistory = new List<GameMoveRecord>();

        // Cell visit tracking (for revisit limits)
        private Dictionary<(int row, int col), int> cellVisitCount = new Dictionary<(int, int), int>();

        // Current max moves limit (set per AI game session)
        private int maxMoves = 200;

        // Run the game with a human player
        public void PlayAsHuman()
        {
            AnsiConsole.Clear();

            AnsiConsole.Write(
                new Rule("[bold cyan]Human Player Mode[/]")
                    .RuleStyle("grey")
                    .Centered());

            AnsiConsole.MarkupLine("[grey]Use[/] [yellow]N/S/E/W[/] [grey]to move,[/] [red]Q[/] [grey]to quit[/]");
            AnsiConsole.WriteLine();

            // Reset game state
            playerRow = 1;
            playerCol = 1;
            moveCount = 0;
            gameWon = false;

            // Main game loop
            while (true)
            {
                // Draw the maze
                DrawMaze();

                // Show current position
                AnsiConsole.MarkupLine($"[grey]Position:[/] [cyan]({playerCol}, {playerRow})[/]");
                AnsiConsole.MarkupLine($"[grey]Moves:[/] [yellow]{moveCount}[/]");
                AnsiConsole.Markup("[green]Enter direction[/] [grey](N/S/E/W or Q)[/]: ");

                // Get player input
                string input = Console.ReadLine() ?? "";
                string direction = input.ToUpper().Trim();

                // Check for quit
                if (direction == "Q")
                {
                    break;
                }

                // Try to move
                if (direction == "N" || direction == "S" || direction == "E" || direction == "W")
                {
                    bool moved = TryMove(direction);

                    if (moved)
                    {
                        moveCount++;

                        // Check for win
                        if (map[playerRow, playerCol] == 2)
                        {
                            gameWon = true;
                            DrawMaze();
                            break;
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[red]You can't go that way! There's a wall.[/]");
                        AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
                        Console.ReadKey(true);
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Invalid direction. Use N, S, E, or W.[/]");
                    AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
                    Console.ReadKey(true);
                }

                AnsiConsole.Clear();
            }
        }

        // Run the game with an AI player
        public async Task PlayAsAi(
            AiPlayer ai,
            string modelId,
            string modelName,
            bool showCoordinates,
            bool showAsciiMap,
            int delayMs,
            bool showAiPrompt,
            bool breadcrumbs,
            bool distanceToWall,
            bool showGoalCoordinates,
            int maxRevisitsPerCell,
            int maxMoves,
            bool reasoningEnabled,
            string reasoningEffort,
            int? reasoningMaxTokens)
        {
            AnsiConsole.Clear();

            AnsiConsole.Write(
                new Rule("[bold cyan]AI Player Mode[/]")
                    .RuleStyle("grey")
                    .Centered());

            AnsiConsole.MarkupLine($"[grey]AI Model:[/] [cyan]{modelName}[/]");
            AnsiConsole.MarkupLine("[grey]Press any key to start, or[/] [red]Q[/] [grey]during play to stop...[/]");
            Console.ReadKey(true);

            // Reset game state
            playerRow = 1;
            playerCol = 1;
            moveCount = 0;
            gameWon = false;
            runningCost = 0m;
            totalTokens = 0;
            moveHistory.Clear();
            cellVisitCount.Clear();
            this.maxMoves = maxMoves;  // Set the max moves limit for this session

            // Track session outcome
            bool stoppedByUser = false;
            bool errorOccurred = false;
            bool tooManyRevisits = false;
            string? errorMessage = null;

            // Start session logging with new settings
            _sessionLogger.StartSession(modelId, modelName, showCoordinates, showAsciiMap, delayMs,
                distanceToWall, showGoalCoordinates, maxRevisitsPerCell, maxMoves,
                reasoningEnabled, reasoningEffort, reasoningMaxTokens);

            // Track the last AI response to display on next iteration
            string? lastAiResponse = null;

            // Main game loop
            while (moveCount < maxMoves)
            {
                AnsiConsole.Clear();

                // Build the prompt for the AI (we need it before drawing if showing prompt)
                string prompt = AiPlayer.BuildPrompt(
                    map, playerRow, playerCol, showCoordinates, showAsciiMap, moveHistory, breadcrumbs,
                    distanceToWall, showGoalCoordinates, maxRevisitsPerCell);

                // Draw game output - either normal or two-column layout
                if (showAiPrompt)
                {
                    DrawTwoColumnLayout(modelName, prompt, lastAiResponse);
                }
                else
                {
                    DrawAiHeader(modelName);
                    DrawMaze();
                    DrawAiStats();
                }

                // Show thinking indicator
                AnsiConsole.MarkupLine("[yellow]🤔 Asking AI for next move...[/]");

                // Get the AI's move with metrics (pass reasoning settings)
                var moveResult = await ai.GetAiMove(prompt, modelId, reasoningEnabled, reasoningEffort, reasoningMaxTokens);

                // Store the AI response for next display
                lastAiResponse = moveResult.RawResponse;

                // Update running totals
                runningCost += moveResult.ActualCostUsd;
                totalTokens += moveResult.TotalTokens;

                // Check for API errors
                if (moveResult.IsError)
                {
                    AnsiConsole.MarkupLine("[red]AI returned an error. Stopping game.[/]");
                    AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
                    Console.ReadKey(true);
                    errorOccurred = true;
                    errorMessage = moveResult.ErrorMessage;
                    break;
                }

                // Handle parse errors (couldn't extract direction from tool call) - retry once
                if (moveResult.Direction == "ERROR")
                {
                    AnsiConsole.MarkupLine("[yellow]⚠️ AI response didn't include valid direction. Retrying...[/]");

                    // Retry once
                    var retryResult = await ai.GetAiMove(prompt, modelId, reasoningEnabled, reasoningEffort, reasoningMaxTokens);
                    lastAiResponse = retryResult.RawResponse;
                    runningCost += retryResult.ActualCostUsd;
                    totalTokens += retryResult.TotalTokens;

                    if (retryResult.Direction == "ERROR")
                    {
                        AnsiConsole.MarkupLine("[red]❌ Retry also failed. Counting as wasted move.[/]");
                        moveResult = retryResult;
                        // Continue with ERROR direction - TryMove will fail and count it as a wall hit
                    }
                    else
                    {
                        moveResult = retryResult;
                        AnsiConsole.MarkupLine($"[green]✓ Retry succeeded![/]");
                    }
                }

                AnsiConsole.MarkupLine($"[green]→[/] AI chose: [bold cyan]{moveResult.Direction}[/]");

                // Store previous position for logging
                int fromRow = playerRow;
                int fromCol = playerCol;

                // Try to move (ERROR directions will fail and count as wall hits)
                bool moved = TryMove(moveResult.Direction);
                moveCount++;

                // Add to move history for next prompt
                moveHistory.Add(new GameMoveRecord
                {
                    MoveNumber = moveCount,
                    Direction = moveResult.Direction,
                    FromRow = fromRow,
                    FromCol = fromCol,
                    ToRow = playerRow,
                    ToCol = playerCol,
                    HitWall = !moved
                });

                // Log the move to session file with reasoning data
                _sessionLogger.LogMove(
                    moveCount,
                    moveResult.Direction,
                    fromRow, fromCol,
                    playerRow, playerCol,
                    moved,
                    moveResult.PromptTokens,
                    moveResult.CompletionTokens,
                    moveResult.ActualCostUsd,
                    moveResult.ReasoningTokens,
                    moveResult.Reasoning
                );

                // Log the raw API request/response for debugging
                _sessionLogger.LogApiCall(
                    moveCount,
                    moveResult.RequestJson,
                    moveResult.ResponseJson,
                    moveResult.HttpStatusCode,
                    moveResult.LatencyMs
                );

                if (moved)
                {
                    // Track cell visits
                    var cellKey = (playerRow, playerCol);
                    if (!cellVisitCount.ContainsKey(cellKey))
                    {
                        cellVisitCount[cellKey] = 1;
                    }
                    else
                    {
                        cellVisitCount[cellKey]++;
                    }

                    // Check if exceeded revisit limit
                    if (cellVisitCount[cellKey] > maxRevisitsPerCell)
                    {
                        AnsiConsole.Clear();
                        DrawAiHeader(modelName);
                        DrawMaze();
                        DrawAiStats();
                        AnsiConsole.MarkupLine($"[red]🔄 AI visited the same cell ({playerCol}, {playerRow}) {cellVisitCount[cellKey]} times![/]");
                        AnsiConsole.MarkupLine($"[yellow]Exceeded max revisits per cell ({maxRevisitsPerCell}). Terminating game.[/]");
                        tooManyRevisits = true;
                        break;
                    }

                    // Check for win
                    if (map[playerRow, playerCol] == 2)
                    {
                        gameWon = true;
                        AnsiConsole.Clear();
                        DrawAiHeader(modelName);
                        DrawMaze();
                        DrawAiStats();
                        AnsiConsole.MarkupLine("[bold green]🎉 THE AI FOUND THE EXIT! 🎉[/]");
                        break;
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]💥 AI tried to walk into a wall![/]");
                }

                // Wait before next move (so humans can watch)
                await Task.Delay(delayMs);

                // Check if user wants to stop
                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q)
                    {
                        AnsiConsole.MarkupLine("[yellow]Stopped by user.[/]");
                        stoppedByUser = true;
                        break;
                    }
                }
            }

            bool reachedMaxMoves = moveCount >= maxMoves;
            if (reachedMaxMoves)
            {
                AnsiConsole.MarkupLine($"[yellow]AI reached maximum moves ({maxMoves}) without finding exit.[/]");
            }

            // End session logging
            _sessionLogger.EndSession(gameWon, stoppedByUser, reachedMaxMoves, tooManyRevisits, errorOccurred, errorMessage);
        }

        // Draw the AI mode header with model info
        private void DrawAiHeader(string modelName)
        {
            AnsiConsole.Write(
                new Rule($"[bold cyan]AI: {modelName}[/]")
                    .RuleStyle("grey")
                    .LeftJustified());
        }

        // Draw AI stats panel with running cost
        private void DrawAiStats()
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn("[yellow]Position[/]").Centered())
                .AddColumn(new TableColumn("[yellow]Moves[/]").Centered())
                .AddColumn(new TableColumn("[yellow]Tokens[/]").Centered())
                .AddColumn(new TableColumn("[yellow]Cost[/]").Centered());

            table.AddRow(
                $"[cyan]({playerCol}, {playerRow})[/]",
                $"[white]{moveCount}[/] / {maxMoves}",
                $"[white]{totalTokens:N0}[/]",
                $"[green]${runningCost:F6}[/]"
            );

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        // Draw a two-column layout: game on left, AI prompt + response on right
        // Uses Spectre.Console Columns for stable layout
        private void DrawTwoColumnLayout(string modelName, string prompt, string? aiResponse = null)
        {
            DrawAiHeader(modelName);

            // Get terminal dimensions for dynamic sizing
            int terminalWidth = Console.WindowWidth;
            int terminalHeight = Console.WindowHeight;

            // Left column is fixed for the maze (maze is 12 cols * 2 chars = 24, plus some padding)
            int leftColumnWidth = 32;
            // Right column gets remaining space minus some padding for borders
            int rightColumnWidth = Math.Max(60, terminalWidth - leftColumnWidth - 5);
            // Max line width for text in right column (leave room for markup)
            int maxLineWidth = rightColumnWidth - 5;

            // Calculate available lines for right panel
            // Reserve: 1 for header, maze height (11 rows), 3 for stats, 2 for "Asking AI...", 2 buffer
            int mazeRows = map.GetLength(0);
            int reservedLines = 1 + mazeRows + 5;
            int availableLines = Math.Max(20, terminalHeight - reservedLines);

            // Build the left column content (maze + stats)
            var leftBuilder = new System.Text.StringBuilder();
            leftBuilder.AppendLine();

            // Add maze
            int rows = map.GetLength(0);
            int cols = map.GetLength(1);
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    if (row == playerRow && col == playerCol)
                        leftBuilder.Append("[bold cyan]P[/] ");
                    else if (map[row, col] == 1)
                        leftBuilder.Append("[grey]#[/] ");
                    else if (map[row, col] == 0)
                        leftBuilder.Append("[white].[/] ");
                    else if (map[row, col] == 2)
                        leftBuilder.Append("[bold green]X[/] ");
                }
                leftBuilder.AppendLine();
            }
            leftBuilder.AppendLine();
            leftBuilder.AppendLine($"[grey]Pos:[/] [cyan]({playerCol},{playerRow})[/] [grey]Moves:[/] {moveCount}/{maxMoves}");
            leftBuilder.AppendLine($"[grey]Cost:[/] [green]${runningCost:F6}[/]");

            // Build the right column content
            var rightBuilder = new System.Text.StringBuilder();
            int linesUsed = 0;

            // Show AI response if available (this is what the AI said)
            // Give response most of the space since that's what the user wants to see
            if (!string.IsNullOrEmpty(aiResponse))
            {
                rightBuilder.AppendLine("[cyan]── AI Response ──[/]");
                linesUsed++;

                var responseLines = aiResponse.Split('\n');
                // Give response 85% of available space - user wants to see more
                int maxResponseLines = Math.Max(20, (int)(availableLines * 0.85));
                int showResponseLines = Math.Min(responseLines.Length, maxResponseLines);

                for (int i = 0; i < showResponseLines; i++)
                {
                    string line = responseLines[i].TrimEnd();
                    line = line.Replace("[", "[[").Replace("]", "]]");
                    if (line.Length > maxLineWidth)
                    {
                        line = line.Substring(0, maxLineWidth - 3) + "...";
                    }
                    rightBuilder.AppendLine("[white]" + line + "[/]");
                    linesUsed++;
                }

                if (responseLines.Length > maxResponseLines)
                {
                    rightBuilder.AppendLine($"[grey]... ({responseLines.Length - maxResponseLines} more lines)[/]");
                    linesUsed++;
                }
                rightBuilder.AppendLine();
                linesUsed++;
            }

            // Show condensed prompt - just the key directional/visibility data (compact)
            rightBuilder.AppendLine("[yellow]── Vision ──[/]");

            // Extract just the important parts of the prompt for display
            var promptLines = prompt.Split('\n');
            int promptLinesShown = 0;

            foreach (var rawLine in promptLines)
            {
                if (promptLinesShown >= 6) break;  // Keep prompt section very compact

                string line = rawLine.TrimEnd();
                // Only show lines with key data: directions, positions, exit visibility
                bool isKeyLine = line.StartsWith("- North:") || line.StartsWith("- South:") ||
                                 line.StartsWith("- East:") || line.StartsWith("- West:") ||
                                 line.Contains("EXIT VISIBLE") || line.Contains("THE EXIT IS");

                if (isKeyLine)
                {
                    line = line.Replace("[", "[[").Replace("]", "]]");
                    if (line.Length > maxLineWidth)
                    {
                        line = line.Substring(0, maxLineWidth - 3) + "...";
                    }
                    rightBuilder.AppendLine("[grey]" + line + "[/]");
                    promptLinesShown++;
                }
            }

            // Use Spectre Columns for side-by-side display
            var table = new Table()
                .Border(TableBorder.None)
                .HideHeaders()
                .AddColumn(new TableColumn("Left").Width(leftColumnWidth))
                .AddColumn(new TableColumn("Right").Width(rightColumnWidth));

            table.AddRow(
                new Markup(leftBuilder.ToString()),
                new Markup(rightBuilder.ToString())
            );

            AnsiConsole.Write(table);
        }

        // Try to move in a direction, returns true if successful
        private bool TryMove(string direction)
        {
            int newRow = playerRow;
            int newCol = playerCol;

            // Calculate new position
            if (direction == "N")
            {
                newRow = playerRow - 1;
            }
            else if (direction == "S")
            {
                newRow = playerRow + 1;
            }
            else if (direction == "E")
            {
                newCol = playerCol + 1;
            }
            else if (direction == "W")
            {
                newCol = playerCol - 1;
            }

            // Check if new position is a wall
            if (map[newRow, newCol] == 1)
            {
                return false;  // Can't move - there's a wall
            }

            // Move is valid - update position
            playerRow = newRow;
            playerCol = newCol;
            return true;
        }

        // Draw the maze to the console with colors
        private void DrawMaze()
        {
            AnsiConsole.WriteLine();

            // Get maze dimensions
            int rows = map.GetLength(0);
            int cols = map.GetLength(1);

            // Build the maze display
            var mazeBuilder = new System.Text.StringBuilder();

            // Draw each row
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    // Check if this is the player's position
                    if (row == playerRow && col == playerCol)
                    {
                        mazeBuilder.Append("[bold cyan]P[/] ");  // P for player
                    }
                    else
                    {
                        int cell = map[row, col];

                        if (cell == 1)
                        {
                            mazeBuilder.Append("[grey]#[/] ");  // Wall
                        }
                        else if (cell == 0)
                        {
                            mazeBuilder.Append("[white].[/] ");  // Open path
                        }
                        else if (cell == 2)
                        {
                            mazeBuilder.Append("[bold green]X[/] ");  // Exit
                        }
                    }
                }

                mazeBuilder.AppendLine();  // New line after each row
            }

            AnsiConsole.Markup(mazeBuilder.ToString());
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Legend:[/] [cyan]P[/]=Player  [grey]#[/]=Wall  [white].[/]=Path  [green]X[/]=Exit");
            AnsiConsole.WriteLine();
        }

        // Get whether the game was won
        public bool WasGameWon()
        {
            return gameWon;
        }

        // Get total moves made
        public int GetMoveCount()
        {
            return moveCount;
        }
    }
}
