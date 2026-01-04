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

        // Show the main menu and get the user's choice
        public string ShowMainMenu()
        {
            Console.Clear();
            Console.WriteLine("========================================");
            Console.WriteLine("       MAZE GAME - LLM EXPERIMENT       ");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("  1. Play as Human");
            Console.WriteLine("  2. Watch AI Play");
            Console.WriteLine("  3. Select AI Model");
            Console.WriteLine("  4. Settings");
            Console.WriteLine("  5. Quit");
            Console.WriteLine();
            Console.WriteLine("Current AI: " + AiPlayer.ModelNames[SelectedModelIndex]);
            Console.WriteLine();
            Console.Write("Enter your choice (1-5): ");

            string choice = Console.ReadLine() ?? "";
            return choice.Trim();
        }

        // Show the AI model selection menu
        public void ShowModelSelectionMenu()
        {
            Console.Clear();
            Console.WriteLine("========================================");
            Console.WriteLine("         SELECT AI MODEL                ");
            Console.WriteLine("========================================");
            Console.WriteLine();

            // Show all available models
            for (int i = 0; i < AiPlayer.ModelNames.Length; i++)
            {
                // Put an arrow next to the currently selected model
                if (i == SelectedModelIndex)
                {
                    Console.WriteLine("  >> " + (i + 1) + ". " + AiPlayer.ModelNames[i] + " (selected)");
                }
                else
                {
                    Console.WriteLine("     " + (i + 1) + ". " + AiPlayer.ModelNames[i]);
                }
            }

            Console.WriteLine();
            Console.WriteLine("     0. Back to Main Menu");
            Console.WriteLine();
            Console.Write("Enter your choice: ");

            string choice = Console.ReadLine() ?? "";

            // Try to parse the choice as a number
            if (int.TryParse(choice, out int modelNumber))
            {
                // Check if it's a valid model number (1 through 5)
                if (modelNumber >= 1 && modelNumber <= AiPlayer.ModelNames.Length)
                {
                    SelectedModelIndex = modelNumber - 1;  // Convert to 0-based index
                    Console.WriteLine();
                    Console.WriteLine("Selected: " + AiPlayer.ModelNames[SelectedModelIndex]);
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey();
                }
            }
        }

        // Show the settings menu
        public void ShowSettingsMenu()
        {
            bool stayInSettings = true;

            while (stayInSettings)
            {
                Console.Clear();
                Console.WriteLine("========================================");
                Console.WriteLine("             SETTINGS                   ");
                Console.WriteLine("========================================");
                Console.WriteLine();
                Console.WriteLine("  1. Show coordinates to AI: " + (ShowCoordinates ? "YES" : "NO"));
                Console.WriteLine("  2. Show ASCII map to AI: " + (ShowAsciiMap ? "YES" : "NO"));
                Console.WriteLine("  3. Delay between moves: " + DelayBetweenMoves + " ms");
                Console.WriteLine();
                Console.WriteLine("  0. Back to Main Menu");
                Console.WriteLine();
                Console.Write("Enter your choice: ");

                string choice = Console.ReadLine() ?? "";

                if (choice == "1")
                {
                    // Toggle the coordinates setting
                    ShowCoordinates = !ShowCoordinates;
                }
                else if (choice == "2")
                {
                    // Toggle the ASCII map setting
                    ShowAsciiMap = !ShowAsciiMap;
                }
                else if (choice == "3")
                {
                    // Ask for new delay value
                    Console.Write("Enter delay in milliseconds (100-2000): ");
                    string delayInput = Console.ReadLine() ?? "";

                    if (int.TryParse(delayInput, out int newDelay))
                    {
                        // Make sure the delay is in a reasonable range
                        if (newDelay >= 100 && newDelay <= 2000)
                        {
                            DelayBetweenMoves = newDelay;
                        }
                        else
                        {
                            Console.WriteLine("Invalid delay. Must be between 100 and 2000.");
                            Console.ReadKey();
                        }
                    }
                }
                else if (choice == "0")
                {
                    stayInSettings = false;
                }
            }
        }

        // Ask user for their OpenRouter API key
        public string AskForApiKey()
        {
            Console.Clear();
            Console.WriteLine("========================================");
            Console.WriteLine("         OPENROUTER API KEY             ");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("To use AI players, you need an OpenRouter API key.");
            Console.WriteLine("Get one free at: https://openrouter.ai/keys");
            Console.WriteLine();
            Console.WriteLine("You can also set the OPENROUTER_API_KEY environment variable.");
            Console.WriteLine();
            Console.Write("Enter your API key (or press Enter to skip): ");

            string key = Console.ReadLine() ?? "";
            return key.Trim();
        }

        // Show a message when the game ends
        public void ShowGameResult(bool won, int moves, string playerType)
        {
            Console.WriteLine();
            Console.WriteLine("========================================");

            if (won)
            {
                Console.WriteLine("  CONGRATULATIONS! The exit was found!");
            }
            else
            {
                Console.WriteLine("  Game ended without finding the exit.");
            }

            Console.WriteLine("  Player: " + playerType);
            Console.WriteLine("  Total moves: " + moves);
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("Press any key to return to menu...");
            Console.ReadKey();
        }
    }
}
