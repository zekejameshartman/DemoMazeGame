namespace DemoMazeGame
{
    // This class contains the main game logic
    // It handles both human players and AI players
    public class Game
    {
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

        // Maximum moves before giving up (prevents infinite loops)
        private int maxMoves = 200;

        // Run the game with a human player
        public void PlayAsHuman()
        {
            Console.Clear();
            Console.WriteLine("=== HUMAN PLAYER MODE ===");
            Console.WriteLine("Use N/S/E/W to move, Q to quit");
            Console.WriteLine();

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
                Console.WriteLine("Position: (" + playerCol + ", " + playerRow + ")");
                Console.WriteLine("Moves: " + moveCount);
                Console.Write("Enter direction (N/S/E/W) or Q to quit: ");

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
                        Console.WriteLine("You can't go that way! There's a wall.");
                        Console.WriteLine("Press any key to continue...");
                        Console.ReadKey();
                    }
                }
                else
                {
                    Console.WriteLine("Invalid direction. Use N, S, E, or W.");
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey();
                }

                Console.Clear();
            }
        }

        // Run the game with an AI player
        public async Task PlayAsAi(AiPlayer ai, string modelId, string modelName, bool showCoordinates, bool showAsciiMap, int delayMs)
        {
            Console.Clear();
            Console.WriteLine("=== AI PLAYER MODE ===");
            Console.WriteLine("AI Model: " + modelName);
            Console.WriteLine("Press any key to start, or Q during play to stop...");
            Console.ReadKey();

            // Reset game state
            playerRow = 1;
            playerCol = 1;
            moveCount = 0;
            gameWon = false;

            // Reset the AI's conversation history for a fresh start
            ai.ResetConversation();

            // Main game loop
            while (moveCount < maxMoves)
            {
                Console.Clear();

                // Draw the maze
                DrawMaze();

                // Show current position
                Console.WriteLine("AI: " + modelName);
                Console.WriteLine("Position: (" + playerCol + ", " + playerRow + ")");
                Console.WriteLine("Moves: " + moveCount);

                // Build the prompt for the AI
                string prompt = AiPlayer.BuildPrompt(map, playerRow, playerCol, showCoordinates, showAsciiMap);

                // Show what we're sending to the AI
                Console.WriteLine();
                Console.WriteLine("--- Asking AI for next move... ---");
                Console.WriteLine($"Prompt: {prompt}");
                // Get the AI's move
                string direction = await ai.GetAiMove(prompt, modelId);

                Console.WriteLine("AI chose: " + direction);

                // Check for errors
                if (direction == "ERROR")
                {
                    Console.WriteLine("AI returned an error. Stopping game.");
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey();
                    break;
                }

                // Try to move
                bool moved = TryMove(direction);

                if (moved)
                {
                    moveCount++;

                    // Check for win
                    if (map[playerRow, playerCol] == 2)
                    {
                        gameWon = true;
                        Console.Clear();
                        DrawMaze();
                        Console.WriteLine("THE AI FOUND THE EXIT!");
                        break;
                    }
                }
                else
                {
                    Console.WriteLine("AI tried to walk into a wall!");
                    moveCount++;  // Count failed moves too
                }

                // Wait before next move (so humans can watch)
                await Task.Delay(delayMs);

                // Check if user wants to stop
                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q)
                    {
                        Console.WriteLine("Stopped by user.");
                        break;
                    }
                }
            }

            if (moveCount >= maxMoves)
            {
                Console.WriteLine("AI reached maximum moves (" + maxMoves + ") without finding exit.");
            }
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

        // Draw the maze to the console
        private void DrawMaze()
        {
            Console.WriteLine();

            // Get maze dimensions
            int rows = map.GetLength(0);
            int cols = map.GetLength(1);

            // Draw each row
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    // Check if this is the player's position
                    if (row == playerRow && col == playerCol)
                    {
                        Console.Write("P ");  // P for player
                    }
                    else
                    {
                        int cell = map[row, col];

                        if (cell == 1)
                        {
                            Console.Write("# ");  // Wall
                        }
                        else if (cell == 0)
                        {
                            Console.Write(". ");  // Open path
                        }
                        else if (cell == 2)
                        {
                            Console.Write("X ");  // Exit
                        }
                    }
                }

                Console.WriteLine();  // New line after each row
            }

            Console.WriteLine();
            Console.WriteLine("Legend: P=Player, #=Wall, .=Path, X=Exit");
            Console.WriteLine();
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
