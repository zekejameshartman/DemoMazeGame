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

        // Maximum moves before giving up (prevents infinite loops)
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
        public async Task PlayAsAi(AiPlayer ai, string modelId, string modelName, bool showCoordinates, bool showAsciiMap, int delayMs)
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

            // Track session outcome
            bool stoppedByUser = false;
            bool errorOccurred = false;
            string? errorMessage = null;

            // Start session logging
            _sessionLogger.StartSession(modelId, modelName, showCoordinates, showAsciiMap, delayMs);

            // Reset the AI's conversation history for a fresh start
            ai.ResetConversation();

            // Main game loop
            while (moveCount < maxMoves)
            {
                AnsiConsole.Clear();

                // Draw header with stats
                DrawAiHeader(modelName);

                // Draw the maze
                DrawMaze();

                // Show current position and stats
                DrawAiStats();

                // Build the prompt for the AI
                string prompt = AiPlayer.BuildPrompt(map, playerRow, playerCol, showCoordinates, showAsciiMap);

                // Show thinking indicator
                AnsiConsole.MarkupLine("[yellow]🤔 Asking AI for next move...[/]");

                // Get the AI's move with metrics
                var moveResult = await ai.GetAiMove(prompt, modelId);

                // Update running totals
                runningCost += moveResult.ActualCostUsd;
                totalTokens += moveResult.TotalTokens;

                AnsiConsole.MarkupLine($"[green]→[/] AI chose: [bold cyan]{moveResult.Direction}[/]");

                // Check for errors
                if (moveResult.IsError)
                {
                    AnsiConsole.MarkupLine("[red]AI returned an error. Stopping game.[/]");
                    AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
                    Console.ReadKey(true);
                    errorOccurred = true;
                    errorMessage = moveResult.ErrorMessage;
                    break;
                }

                // Store previous position for logging
                int fromRow = playerRow;
                int fromCol = playerCol;

                // Try to move
                bool moved = TryMove(moveResult.Direction);
                moveCount++;

                // Log the move
                _sessionLogger.LogMove(
                    moveCount,
                    moveResult.Direction,
                    fromRow, fromCol,
                    playerRow, playerCol,
                    moved,
                    moveResult.PromptTokens,
                    moveResult.CompletionTokens,
                    moveResult.ActualCostUsd
                );

                if (moved)
                {
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
            _sessionLogger.EndSession(gameWon, stoppedByUser, reachedMaxMoves, errorOccurred, errorMessage);
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
