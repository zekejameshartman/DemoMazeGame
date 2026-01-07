using Spectre.Console;

namespace DemoMazeGame
{
    // This class handles all the menu screens in the game
    public class Menu
    {
        // Settings that can be changed from the menu
        public int SelectedModelIndex = 0;      // Which AI model is selected
        public bool ShowCoordinates = false;    // Whether to show coordinates to AI
        public bool ShowAsciiMap = false;       // Whether to show ASCII map to AI
        public int DelayBetweenMoves = 500;     // Milliseconds to wait between AI moves
        public bool ShowAiPrompt = false;       // Whether to show the prompt sent to AI
        public bool Breadcrumbs = false;        // Whether to add breadcrumb hints about revisited spots

        // New prompt optimization settings
        public bool DistanceToWall = true;      // Whether to show distance to walls in each direction
        public bool ShowGoalCoordinates = true; // Whether to show goal position to AI
        public int MaxRevisitsPerCell = 10;     // Max times AI can revisit same cell before failure
        public int MaxMoves = 200;              // Max total moves before failure

        // Reasoning token settings
        public bool ReasoningEnabled = true;    // Whether to enable reasoning tokens
        public string ReasoningEffort = "medium"; // Effort level: xhigh, high, medium, low, minimal, none
        public int? ReasoningMaxTokens = null;  // Max tokens for reasoning (null = use effort instead)

        // Show the main menu and get the user's choice
        public string ShowMainMenu()
        {
            AnsiConsole.Clear();

            // Display header
            AnsiConsole.Write(
                new FigletText("Maze Game")
                    .Centered()
                    .Color(Color.Cyan1));

            AnsiConsole.Write(
                new Rule("[bold yellow]LLM Spatial Reasoning Experiment[/]")
                    .RuleStyle("grey")
                    .Centered());

            AnsiConsole.WriteLine();

            // Show current AI model
            AnsiConsole.MarkupLine($"[grey]Current AI:[/] [cyan]{AiPlayer.ModelNames[SelectedModelIndex]}[/]");
            AnsiConsole.WriteLine();

            // Create selection prompt
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[green]What would you like to do?[/]")
                    .PageSize(10)
                    .HighlightStyle(new Style(Color.Cyan1, decoration: Decoration.Bold))
                    .AddChoices(new[]
                    {
                        "[bold]1[/] Play as Human",
                        "[bold]2[/] Watch AI Play",
                        "[bold]3[/] Select AI Model",
                        "[bold]4[/] Settings",
                        "[bold]5[/] Quit"
                    }));

            // Extract the number from the choice
            if (choice.Contains("1")) return "1";
            if (choice.Contains("2")) return "2";
            if (choice.Contains("3")) return "3";
            if (choice.Contains("4")) return "4";
            if (choice.Contains("5")) return "5";
            return "";
        }

        // Show the AI model selection menu
        public void ShowModelSelectionMenu()
        {
            AnsiConsole.Clear();

            AnsiConsole.Write(
                new Rule("[bold cyan]Select AI Model[/]")
                    .RuleStyle("grey")
                    .Centered());

            AnsiConsole.WriteLine();

            // Build choices with current selection indicator
            var choices = new List<string>();
            for (int i = 0; i < AiPlayer.ModelNames.Length; i++)
            {
                string indicator = i == SelectedModelIndex ? " [green](current)[/]" : "";
                choices.Add($"{AiPlayer.ModelNames[i]}{indicator}");
            }
            choices.Add("[grey]← Back to Main Menu[/]");

            var selection = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]Choose an AI model:[/]")
                    .PageSize(10)
                    .HighlightStyle(new Style(Color.Cyan1, decoration: Decoration.Bold))
                    .AddChoices(choices));

            // Find which model was selected
            for (int i = 0; i < AiPlayer.ModelNames.Length; i++)
            {
                if (selection.Contains(AiPlayer.ModelNames[i]))
                {
                    SelectedModelIndex = i;
                    AnsiConsole.MarkupLine($"\n[green]✓[/] Selected: [cyan]{AiPlayer.ModelNames[i]}[/]");
                    Thread.Sleep(800);
                    break;
                }
            }
        }

        // Show the settings menu
        public void ShowSettingsMenu()
        {
            bool stayInSettings = true;

            while (stayInSettings)
            {
                AnsiConsole.Clear();

                AnsiConsole.Write(
                    new Rule("[bold cyan]Settings[/]")
                        .RuleStyle("grey")
                        .Centered());

                AnsiConsole.WriteLine();

                // Show current settings in a table
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .AddColumn(new TableColumn("[yellow]Setting[/]").Centered())
                    .AddColumn(new TableColumn("[yellow]Value[/]").Centered());

                table.AddRow(
                    "Show coordinates to AI",
                    ShowCoordinates ? "[green]YES[/]" : "[red]NO[/]");
                table.AddRow(
                    "Show ASCII map to AI",
                    ShowAsciiMap ? "[green]YES[/]" : "[red]NO[/]");
                table.AddRow(
                    "Show AI prompt",
                    ShowAiPrompt ? "[green]YES[/]" : "[red]NO[/]");
                table.AddRow(
                    "Breadcrumbs",
                    Breadcrumbs ? "[green]YES[/]" : "[red]NO[/]");
                table.AddRow(
                    "Distance to walls",
                    DistanceToWall ? "[green]YES[/]" : "[red]NO[/]");
                table.AddRow(
                    "Show goal coordinates",
                    ShowGoalCoordinates ? "[green]YES[/]" : "[red]NO[/]");
                table.AddRow(
                    "Max revisits per cell",
                    $"[cyan]{MaxRevisitsPerCell}[/]");
                table.AddRow(
                    "Max moves",
                    $"[cyan]{MaxMoves}[/]");
                table.AddRow(
                    "Delay between moves",
                    $"[cyan]{DelayBetweenMoves}[/] ms");
                table.AddRow(
                    "Reasoning tokens",
                    ReasoningEnabled ? "[green]ENABLED[/]" : "[red]DISABLED[/]");
                table.AddRow(
                    "Reasoning effort",
                    $"[cyan]{ReasoningEffort}[/]");
                table.AddRow(
                    "Reasoning max tokens",
                    ReasoningMaxTokens.HasValue ? $"[cyan]{ReasoningMaxTokens.Value}[/]" : "[grey]auto (use effort)[/]");

                AnsiConsole.Write(table);
                AnsiConsole.WriteLine();

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[green]Select a setting to change:[/]")
                        .PageSize(15)
                        .HighlightStyle(new Style(Color.Cyan1, decoration: Decoration.Bold))
                        .AddChoices(new[]
                        {
                            "[bold]1[/] Toggle coordinates",
                            "[bold]2[/] Toggle ASCII map",
                            "[bold]3[/] Toggle show AI prompt",
                            "[bold]4[/] Toggle breadcrumbs",
                            "[bold]5[/] Toggle distance to walls",
                            "[bold]6[/] Toggle show goal coordinates",
                            "[bold]7[/] Change max revisits per cell",
                            "[bold]8[/] Change max moves",
                            "[bold]9[/] Change delay",
                            "[bold]10[/] Toggle reasoning tokens",
                            "[bold]11[/] Change reasoning effort",
                            "[bold]12[/] Set reasoning max tokens",
                            "[grey]← Back to Main Menu[/]"
                        }));

                if (choice.Contains("1") && !choice.Contains("10") && !choice.Contains("11") && !choice.Contains("12"))
                {
                    ShowCoordinates = !ShowCoordinates;
                }
                else if (choice.Contains("2") && !choice.Contains("12"))
                {
                    ShowAsciiMap = !ShowAsciiMap;
                }
                else if (choice.Contains("3"))
                {
                    ShowAiPrompt = !ShowAiPrompt;
                }
                else if (choice.Contains("4"))
                {
                    Breadcrumbs = !Breadcrumbs;
                }
                else if (choice.Contains("5"))
                {
                    DistanceToWall = !DistanceToWall;
                }
                else if (choice.Contains("6"))
                {
                    ShowGoalCoordinates = !ShowGoalCoordinates;
                }
                else if (choice.Contains("7"))
                {
                    var newMaxRevisits = AnsiConsole.Prompt(
                        new TextPrompt<int>("[yellow]Enter max revisits per cell[/] [grey](1-100)[/]:")
                            .DefaultValue(MaxRevisitsPerCell)
                            .ValidationErrorMessage("[red]Please enter a valid number[/]")
                            .Validate(revisits =>
                            {
                                return revisits switch
                                {
                                    < 1 => ValidationResult.Error("[red]Must be at least 1[/]"),
                                    > 100 => ValidationResult.Error("[red]Must be at most 100[/]"),
                                    _ => ValidationResult.Success()
                                };
                            }));
                    MaxRevisitsPerCell = newMaxRevisits;
                }
                else if (choice.Contains("8") && !choice.Contains("18"))
                {
                    var newMaxMoves = AnsiConsole.Prompt(
                        new TextPrompt<int>("[yellow]Enter max total moves[/] [grey](10-1000)[/]:")
                            .DefaultValue(MaxMoves)
                            .ValidationErrorMessage("[red]Please enter a valid number[/]")
                            .Validate(moves =>
                            {
                                return moves switch
                                {
                                    < 10 => ValidationResult.Error("[red]Must be at least 10[/]"),
                                    > 1000 => ValidationResult.Error("[red]Must be at most 1000[/]"),
                                    _ => ValidationResult.Success()
                                };
                            }));
                    MaxMoves = newMaxMoves;
                }
                else if (choice.Contains("9"))
                {
                    var newDelay = AnsiConsole.Prompt(
                        new TextPrompt<int>("[yellow]Enter delay in milliseconds[/] [grey](100-2000)[/]:")
                            .DefaultValue(DelayBetweenMoves)
                            .ValidationErrorMessage("[red]Please enter a valid number[/]")
                            .Validate(delay =>
                            {
                                return delay switch
                                {
                                    < 100 => ValidationResult.Error("[red]Delay must be at least 100ms[/]"),
                                    > 2000 => ValidationResult.Error("[red]Delay must be at most 2000ms[/]"),
                                    _ => ValidationResult.Success()
                                };
                            }));
                    DelayBetweenMoves = newDelay;
                }
                else if (choice.Contains("10"))
                {
                    ReasoningEnabled = !ReasoningEnabled;
                }
                else if (choice.Contains("11"))
                {
                    var effortChoice = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[yellow]Select reasoning effort level:[/]")
                            .HighlightStyle(new Style(Color.Cyan1))
                            .AddChoices(new[]
                            {
                                "xhigh - Maximum reasoning (95% of max_tokens)",
                                "high - High reasoning (80% of max_tokens)",
                                "medium - Medium reasoning (50% of max_tokens) [default]",
                                "low - Low reasoning (20% of max_tokens)",
                                "minimal - Minimal reasoning (10% of max_tokens)",
                                "none - No reasoning"
                            }));

                    ReasoningEffort = effortChoice.Split(' ')[0]; // Extract just the effort level
                }
                else if (choice.Contains("12"))
                {
                    var maxTokensChoice = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[yellow]Set max tokens for reasoning:[/]")
                            .HighlightStyle(new Style(Color.Cyan1))
                            .AddChoices(new[]
                            {
                                "Auto (use effort level instead)",
                                "1024 tokens",
                                "2000 tokens",
                                "4000 tokens",
                                "8000 tokens",
                                "16000 tokens",
                                "32000 tokens (max for Anthropic)",
                                "Custom value..."
                            }));

                    if (maxTokensChoice.Contains("Auto"))
                    {
                        ReasoningMaxTokens = null;
                    }
                    else if (maxTokensChoice.Contains("Custom"))
                    {
                        var customValue = AnsiConsole.Prompt(
                            new TextPrompt<int>("[yellow]Enter max tokens[/] [grey](1024-32000)[/]:")
                                .DefaultValue(2000)
                                .ValidationErrorMessage("[red]Please enter a valid number[/]")
                                .Validate(tokens =>
                                {
                                    return tokens switch
                                    {
                                        < 1024 => ValidationResult.Error("[red]Must be at least 1024[/]"),
                                        > 32000 => ValidationResult.Error("[red]Must be at most 32000[/]"),
                                        _ => ValidationResult.Success()
                                    };
                                }));
                        ReasoningMaxTokens = customValue;
                    }
                    else
                    {
                        // Extract the number from choices like "1024 tokens"
                        var numberStr = maxTokensChoice.Split(' ')[0];
                        ReasoningMaxTokens = int.Parse(numberStr);
                    }
                }
                else if (choice.Contains("Back"))
                {
                    stayInSettings = false;
                }
            }
        }

        // Ask user for their OpenRouter API key
        public string AskForApiKey()
        {
            AnsiConsole.Clear();

            AnsiConsole.Write(
                new Rule("[bold cyan]OpenRouter API Key[/]")
                    .RuleStyle("grey")
                    .Centered());

            AnsiConsole.WriteLine();

            var panel = new Panel(
                new Markup(
                    "[yellow]To use AI players, you need an OpenRouter API key.[/]\n\n" +
                    "Get one free at: [link=https://openrouter.ai/keys][cyan]https://openrouter.ai/keys[/][/]\n\n" +
                    "[grey]You can also set the OPENROUTER_API_KEY environment variable.[/]"))
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Grey)
                .Padding(1, 1);

            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();

            var key = AnsiConsole.Prompt(
                new TextPrompt<string>("[green]Enter your API key[/] [grey](or press Enter to skip)[/]:")
                    .AllowEmpty());

            return key.Trim();
        }

        // Show a message when the game ends
        public void ShowGameResult(bool won, int moves, string playerType)
        {
            AnsiConsole.WriteLine();

            if (won)
            {
                AnsiConsole.Write(
                    new Panel(
                        new Markup(
                            "[bold green]🎉 CONGRATULATIONS! 🎉[/]\n\n" +
                            "[green]The exit was found![/]\n\n" +
                            $"[grey]Player:[/] [cyan]{playerType}[/]\n" +
                            $"[grey]Total moves:[/] [yellow]{moves}[/]"))
                        .Border(BoxBorder.Double)
                        .BorderColor(Color.Green)
                        .Padding(2, 1)
                        .Header("[bold green] Victory! [/]"));
            }
            else
            {
                AnsiConsole.Write(
                    new Panel(
                        new Markup(
                            "[yellow]Game ended without finding the exit.[/]\n\n" +
                            $"[grey]Player:[/] [cyan]{playerType}[/]\n" +
                            $"[grey]Total moves:[/] [yellow]{moves}[/]"))
                        .Border(BoxBorder.Rounded)
                        .BorderColor(Color.Yellow)
                        .Padding(2, 1)
                        .Header("[bold yellow] Game Over [/]"));
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return to menu...[/]");
            Console.ReadKey(true);
        }
    }
}
