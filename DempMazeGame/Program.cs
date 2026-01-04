using Microsoft.Extensions.DependencyInjection;
using DemoMazeGame.Services;
using Spectre.Console;

namespace DemoMazeGame
{
    // This is the main entry point for the maze game
    // It creates the menu and handles the main application loop
    internal class Program
    {
        // Path to the .env file (in the project directory, already gitignored)
        private static readonly string EnvFilePath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", ".env");

        static async Task Main(string[] args)
        {
            // Set up dependency injection
            var services = new ServiceCollection();
            services.AddSingleton<IAppLogger, AppLogger>();
            services.AddSingleton<IAiSessionLogger, AiSessionLogger>();
            var serviceProvider = services.BuildServiceProvider();

            // Get services
            var appLogger = serviceProvider.GetRequiredService<IAppLogger>();
            var sessionLogger = serviceProvider.GetRequiredService<IAiSessionLogger>();

            appLogger.LogInfo("Application started");

            try
            {
                // Create the menu system
                Menu menu = new Menu();

                // Try to get API key: environment variable first, then .env file
                string apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? "";
                if (string.IsNullOrEmpty(apiKey))
                {
                    apiKey = LoadApiKeyFromEnvFile();
                }

                // AI player (created later if we have an API key)
                AiPlayer? aiPlayer = null;

                // Main application loop
                bool keepRunning = true;

                while (keepRunning)
                {
                    // Show main menu and get user choice
                    string choice = menu.ShowMainMenu();

                    if (choice == "1")
                    {
                        // Play as Human
                        Game game = new Game(appLogger, sessionLogger);
                        game.PlayAsHuman();
                        menu.ShowGameResult(game.WasGameWon(), game.GetMoveCount(), "Human");
                    }
                    else if (choice == "2")
                    {
                        // Watch AI Play
                        // First, make sure we have an API key
                        if (string.IsNullOrEmpty(apiKey))
                        {
                            apiKey = menu.AskForApiKey();
                            if (!string.IsNullOrEmpty(apiKey))
                            {
                                SaveApiKeyToEnvFile(apiKey);
                            }
                        }

                        if (string.IsNullOrEmpty(apiKey))
                        {
                            appLogger.LogWarning("No API key provided for AI player");
                            AnsiConsole.MarkupLine("[red]No API key provided. Cannot run AI player.[/]");
                            AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
                            Console.ReadKey(true);
                        }
                        else
                        {
                            // Create AI player if we haven't yet
                            if (aiPlayer == null)
                            {
                                aiPlayer = new AiPlayer(apiKey, appLogger);
                            }

                            // Get the selected model
                            string modelId = AiPlayer.ModelIds[menu.SelectedModelIndex];
                            string modelName = AiPlayer.ModelNames[menu.SelectedModelIndex];

                            // Run the game with AI
                            Game game = new Game(appLogger, sessionLogger);
                            await game.PlayAsAi(
                                aiPlayer,
                                modelId,
                                modelName,
                                menu.ShowCoordinates,
                                menu.ShowAsciiMap,
                                menu.DelayBetweenMoves
                            );
                            menu.ShowGameResult(game.WasGameWon(), game.GetMoveCount(), modelName);
                        }
                    }
                    else if (choice == "3")
                    {
                        // Select AI Model
                        menu.ShowModelSelectionMenu();
                    }
                    else if (choice == "4")
                    {
                        // Settings
                        menu.ShowSettingsMenu();
                    }
                    else if (choice == "5")
                    {
                        // Quit
                        keepRunning = false;
                    }
                }

                appLogger.LogInfo("Application exiting normally");
                AnsiConsole.MarkupLine("[cyan]Thanks for playing![/] [yellow]Good luck with your science fair project![/]");
            }
            catch (Exception ex)
            {
                appLogger.LogError("Unhandled exception in main loop", ex);
                AnsiConsole.MarkupLine("[red]An error occurred. Check logs/app.log for details.[/]");
                throw;
            }
        }

        // Load the API key from the .env file if it exists
        private static string LoadApiKeyFromEnvFile()
        {
            try
            {
                if (File.Exists(EnvFilePath))
                {
                    foreach (string line in File.ReadAllLines(EnvFilePath))
                    {
                        if (line.StartsWith("OPENROUTER_API_KEY="))
                        {
                            return line.Substring("OPENROUTER_API_KEY=".Length).Trim();
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors reading the file
            }
            return "";
        }

        // Save the API key to the .env file
        private static void SaveApiKeyToEnvFile(string apiKey)
        {
            try
            {
                File.WriteAllText(EnvFilePath, "OPENROUTER_API_KEY=" + apiKey + Environment.NewLine);
            }
            catch
            {
                // Ignore errors writing the file
            }
        }
    }
}
