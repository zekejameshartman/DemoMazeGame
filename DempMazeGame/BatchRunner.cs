using DemoMazeGame.Services;
using Spectre.Console;

namespace DemoMazeGame
{
    // Orchestrates running multiple AI sessions concurrently with progress visualization
    public class BatchRunner
    {
        private readonly string _apiKey;
        private readonly IAppLogger _appLogger;

        public BatchRunner(string apiKey, IAppLogger appLogger)
        {
            _apiKey = apiKey;
            _appLogger = appLogger;
        }

        // Show batch configuration UI and run the batch
        public async Task<BatchResult> ConfigureAndRun(Menu settings)
        {
            AnsiConsole.Clear();

            AnsiConsole.Write(
                new FigletText("Batch Runner")
                    .Centered()
                    .Color(Color.Cyan1));

            AnsiConsole.Write(
                new Rule("[bold yellow]Run Multiple AI Sessions[/]")
                    .RuleStyle("grey")
                    .Centered());

            AnsiConsole.WriteLine();

            // Model selection
            var modelChoices = new List<string>();
            for (int i = 0; i < AiPlayer.ModelNames.Length; i++)
            {
                string indicator = i == settings.SelectedModelIndex ? " [green](current)[/]" : "";
                modelChoices.Add($"{AiPlayer.ModelNames[i]}{indicator}");
            }

            var modelSelection = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]Select AI model for batch:[/]")
                    .PageSize(10)
                    .HighlightStyle(new Style(Color.Cyan1, decoration: Decoration.Bold))
                    .AddChoices(modelChoices));

            // Find selected model index
            int modelIndex = 0;
            for (int i = 0; i < AiPlayer.ModelNames.Length; i++)
            {
                if (modelSelection.Contains(AiPlayer.ModelNames[i]))
                {
                    modelIndex = i;
                    break;
                }
            }

            string modelId = AiPlayer.ModelIds[modelIndex];
            string modelName = AiPlayer.ModelNames[modelIndex];

            AnsiConsole.WriteLine();

            // Number of sessions
            int totalSessions = AnsiConsole.Prompt(
                new TextPrompt<int>("[yellow]How many sessions to run?[/]")
                    .DefaultValue(10)
                    .ValidationErrorMessage("[red]Please enter a number between 1 and 100[/]")
                    .Validate(n => n >= 1 && n <= 100
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Must be between 1 and 100[/]")));

            // Concurrent sessions
            int maxConcurrent = AnsiConsole.Prompt(
                new TextPrompt<int>("[yellow]How many to run at once (concurrent)?[/]")
                    .DefaultValue(3)
                    .ValidationErrorMessage("[red]Please enter a number between 1 and 10[/]")
                    .Validate(n => n >= 1 && n <= 10
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Must be between 1 and 10[/]")));

            AnsiConsole.WriteLine();

            // Show current settings that will be used
            var settingsTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .Title("[yellow]Batch Settings[/]")
                .AddColumn("Setting")
                .AddColumn("Value");

            settingsTable.AddRow("Model", $"[cyan]{modelName}[/]");
            settingsTable.AddRow("Sessions", $"[white]{totalSessions}[/]");
            settingsTable.AddRow("Concurrent", $"[white]{maxConcurrent}[/]");
            settingsTable.AddRow("Max Moves", $"[white]{settings.MaxMoves}[/]");
            settingsTable.AddRow("Max Revisits", $"[white]{settings.MaxRevisitsPerCell}[/]");
            settingsTable.AddRow("Goal Coords", settings.ShowGoalCoordinates ? "[green]ON[/]" : "[red]OFF[/]");
            settingsTable.AddRow("Breadcrumbs", settings.Breadcrumbs ? "[green]ON[/]" : "[red]OFF[/]");

            AnsiConsole.Write(settingsTable);
            AnsiConsole.WriteLine();

            // Confirm
            if (!AnsiConsole.Confirm("[yellow]Start batch run?[/]"))
            {
                return new BatchResult { Cancelled = true };
            }

            AnsiConsole.WriteLine();

            // Run the batch
            return await RunBatch(
                modelId, modelName,
                totalSessions, maxConcurrent,
                settings);
        }

        // Run the batch with progress bars
        private async Task<BatchResult> RunBatch(
            string modelId,
            string modelName,
            int totalSessions,
            int maxConcurrent,
            Menu settings)
        {
            var results = new List<SessionResult>();
            var sessionTasks = new List<(int index, ProgressTask task)>();
            var cts = new CancellationTokenSource();

            // Set up cancellation on Q key
            _ = Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Q)
                        {
                            cts.Cancel();
                            break;
                        }
                    }
                    Thread.Sleep(100);
                }
            });

            AnsiConsole.MarkupLine("[grey]Press Q to cancel...[/]");
            AnsiConsole.WriteLine();

            await AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn(),
                })
                .StartAsync(async ctx =>
                {
                    // Create progress tasks for each session
                    var progressTasks = new ProgressTask[totalSessions];
                    for (int i = 0; i < totalSessions; i++)
                    {
                        progressTasks[i] = ctx.AddTask($"[grey]Session {i + 1}[/]", maxValue: settings.MaxMoves);
                        progressTasks[i].StartTask();
                    }

                    // Run sessions with limited concurrency
                    var semaphore = new SemaphoreSlim(maxConcurrent);
                    var runningTasks = new Task[totalSessions];

                    for (int i = 0; i < totalSessions; i++)
                    {
                        int sessionIndex = i;
                        runningTasks[i] = Task.Run(async () =>
                        {
                            await semaphore.WaitAsync(cts.Token);
                            try
                            {
                                if (cts.Token.IsCancellationRequested)
                                {
                                    progressTasks[sessionIndex].Description = $"[yellow]Session {sessionIndex + 1} - Cancelled[/]";
                                    progressTasks[sessionIndex].StopTask();
                                    return;
                                }

                                // Update description to show running
                                progressTasks[sessionIndex].Description = $"[white]Session {sessionIndex + 1}[/]";

                                // Create fresh instances for this session
                                var ai = new AiPlayer(_apiKey, _appLogger);
                                var sessionLogger = new AiSessionLogger(_appLogger);
                                var game = new Game(_appLogger, sessionLogger);

                                // Track if breadcrumbs are enabled for display
                                bool showBreadcrumbs = settings.Breadcrumbs;

                                // Run the session
                                var result = await game.RunHeadlessSession(
                                    ai,
                                    modelId,
                                    modelName,
                                    settings.ShowCoordinates,
                                    settings.ShowAsciiMap,
                                    settings.Breadcrumbs,
                                    settings.DistanceToWall,
                                    settings.ShowGoalCoordinates,
                                    settings.MaxRevisitsPerCell,
                                    settings.MaxMoves,
                                    settings.ReasoningEnabled,
                                    settings.ReasoningEffort,
                                    settings.ReasoningMaxTokens,
                                    (progress) =>
                                    {
                                        progressTasks[sessionIndex].Value = progress.MoveNumber;

                                        // Build info string: Move #, Distance, Breadcrumbs (if enabled)
                                        string info = $"Move {progress.MoveNumber}/{progress.MaxMoves} | Dist: {progress.DistanceToExit}";
                                        if (showBreadcrumbs && progress.MaxBreadcrumbs > 0)
                                        {
                                            info += $" | Crumbs: {progress.MaxBreadcrumbs}";
                                        }

                                        progressTasks[sessionIndex].Description = $"[white]S{sessionIndex + 1}[/] [grey]{info}[/]";
                                    },
                                    cts.Token);

                                // Store result
                                lock (results)
                                {
                                    results.Add(result);
                                }

                                // Update final status with color
                                if (result.Won)
                                {
                                    progressTasks[sessionIndex].Description = $"[green]Session {sessionIndex + 1} - WON! ({result.TotalMoves} moves)[/]";
                                    progressTasks[sessionIndex].Value = settings.MaxMoves; // Fill to 100%
                                }
                                else
                                {
                                    string reason = result.FailureReason switch
                                    {
                                        "max_moves" => "Max moves",
                                        "revisit_limit" => "Loop detected",
                                        "error" => "Error",
                                        "cancelled" => "Cancelled",
                                        _ => "Failed"
                                    };
                                    progressTasks[sessionIndex].Description = $"[red]Session {sessionIndex + 1} - {reason}[/]";
                                }

                                progressTasks[sessionIndex].StopTask();
                            }
                            catch (OperationCanceledException)
                            {
                                progressTasks[sessionIndex].Description = $"[yellow]Session {sessionIndex + 1} - Cancelled[/]";
                                progressTasks[sessionIndex].StopTask();
                            }
                            catch (Exception ex)
                            {
                                progressTasks[sessionIndex].Description = $"[red]Session {sessionIndex + 1} - Error: {ex.Message}[/]";
                                progressTasks[sessionIndex].StopTask();
                                _appLogger.LogError($"Batch session {sessionIndex + 1} failed", ex);
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        });
                    }

                    // Wait for all tasks
                    try
                    {
                        await Task.WhenAll(runningTasks);
                    }
                    catch (Exception)
                    {
                        // Errors already handled per-task
                    }
                });

            cts.Cancel(); // Stop the key listener

            // Build and return results
            return new BatchResult
            {
                ModelName = modelName,
                TotalSessions = totalSessions,
                Results = results,
                Cancelled = cts.IsCancellationRequested && results.Count < totalSessions
            };
        }

        // Display summary after batch completes
        public void ShowSummary(BatchResult batch)
        {
            AnsiConsole.WriteLine();

            if (batch.Cancelled && batch.Results.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Batch was cancelled before any sessions completed.[/]");
                return;
            }

            var results = batch.Results;
            int wins = results.Count(r => r.Won);
            int maxMovesFailed = results.Count(r => r.FailureReason == "max_moves");
            int revisitFailed = results.Count(r => r.FailureReason == "revisit_limit");
            int errors = results.Count(r => r.FailureReason == "error");
            int cancelled = results.Count(r => r.FailureReason == "cancelled");

            decimal totalCost = results.Sum(r => r.Cost);
            double avgMoves = results.Count > 0 ? results.Average(r => r.TotalMoves) : 0;
            double avgBacktracks = results.Count > 0 ? results.Average(r => r.Backtracks) : 0;
            double avgCollisions = results.Count > 0 ? results.Average(r => r.WallCollisions) : 0;
            double avgUniquePositions = results.Count > 0 ? results.Average(r => r.UniquePositions) : 0;

            double winRate = results.Count > 0 ? (double)wins / results.Count * 100 : 0;

            var panel = new Panel(
                new Rows(
                    new Markup($"[white]Total Sessions:[/]  [cyan]{results.Count}[/]"),
                    new Markup($"[white]Wins:[/]            [green]{wins}[/] [grey]({winRate:F1}%)[/]"),
                    new Markup($"[white]Failures:[/]        [red]{results.Count - wins}[/]"),
                    new Markup($"  [grey]- Max moves:[/]   [yellow]{maxMovesFailed}[/]"),
                    new Markup($"  [grey]- Loop limit:[/]  [yellow]{revisitFailed}[/]"),
                    new Markup($"  [grey]- Errors:[/]      [yellow]{errors}[/]"),
                    cancelled > 0 ? new Markup($"  [grey]- Cancelled:[/]   [yellow]{cancelled}[/]") : new Markup(""),
                    new Markup(""),
                    new Markup($"[white]Avg Moves:[/]       [cyan]{avgMoves:F1}[/]"),
                    new Markup($"[white]Avg Backtracks:[/]  [cyan]{avgBacktracks:F1}[/]"),
                    new Markup($"[white]Avg Collisions:[/]  [cyan]{avgCollisions:F1}[/]"),
                    new Markup($"[white]Avg Unique Pos:[/]  [cyan]{avgUniquePositions:F1}[/]"),
                    new Markup(""),
                    new Markup($"[white]Total Cost:[/]      [green]${totalCost:F4}[/]")
                ))
                .Header($"[bold cyan]BATCH RESULTS: {batch.ModelName}[/]")
                .Border(BoxBorder.Double)
                .BorderColor(Color.Cyan1)
                .Padding(1, 1);

            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine("[grey]Session logs saved to logs/sessions/[/]");
            AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
            Console.ReadKey(true);
        }
    }

    // Aggregate results from a batch run
    public class BatchResult
    {
        public string ModelName { get; set; } = "";
        public int TotalSessions { get; set; }
        public List<SessionResult> Results { get; set; } = new();
        public bool Cancelled { get; set; }
    }
}
