using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DemoMazeGame.Models;
using DemoMazeGame.Services;

namespace DemoMazeGame
{
    // This class handles all communication with AI models through OpenRouter
    public class AiPlayer
    {
        // HttpClient is used to make web requests to the API
        private HttpClient httpClient;

        // The API key is needed to authenticate with OpenRouter
        private string apiKey;

        // Logger for errors and events
        private readonly IAppLogger _logger;

        // System prompt loaded from file - explains the MOVE: X format
        private readonly string _systemPrompt;

        // List of available AI models we can test
        // Each model has a display name and the actual model ID used by OpenRouter
        public static readonly string[] ModelNames =
        {
            "Claude Haiku 4.5",
            "GPT-4o Mini",
            "Gemini 2.0 Flash",
            "DeepSeek R1-0528:free",
            "Llama 4 Scout"
        };

        public static readonly string[] ModelIds =
        {
            "anthropic/claude-haiku-4.5",
            "openai/gpt-4o-mini",
            "google/gemini-2.0-flash-001",
            "deepseek/deepseek-r1-0528:free",
            "meta-llama/llama-4-scout"
        };

        // Constructor - runs when we create a new AiPlayer
        public AiPlayer(string openRouterApiKey, IAppLogger logger)
        {
            apiKey = openRouterApiKey;
            _logger = logger;

            // Load system prompt from file
            string systemPromptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "system_prompt.txt");
            if (File.Exists(systemPromptPath))
            {
                _systemPrompt = File.ReadAllText(systemPromptPath);
                _logger.LogInfo("System prompt loaded from file");
            }
            else
            {
                // Fallback if file not found
                _systemPrompt = "You are navigating a maze. When you decide on a direction, respond with MOVE: followed by N, S, E, or W.";
                _logger.LogWarning($"System prompt file not found at {systemPromptPath}, using fallback");
            }

            // Create the HTTP client and set up the headers
            httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/demo-maze-game");
            httpClient.DefaultRequestHeaders.Add("X-Title", "Demo Maze Game");
        }

        // This method asks the AI which direction to move
        // Returns AiMoveResult with direction and token/cost metrics
        // Each call sends only the single prompt - no conversation history
        public async Task<AiMoveResult> GetAiMove(string prompt, string modelId)
        {
            var result = new AiMoveResult();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Build the messages array with system prompt + user message
                var messages = new object[]
                {
                    new { role = "system", content = _systemPrompt },
                    new { role = "user", content = prompt }
                };

                // Build the request body as a JSON object
                // OpenRouter uses the same format as OpenAI's chat API
                // We include usage.include = true to get actual cost from OpenRouter
                var requestBody = new
                {
                    model = modelId,
                    messages = messages,
                    max_tokens = 500,  // Allow reasoning models to think (they must end with "MOVE: X")
                    temperature = 0.1,  // Low temperature = more consistent responses
                    usage = new { include = true }  // Request actual cost from OpenRouter
                };

                // Convert the request to JSON
                string jsonRequest = JsonSerializer.Serialize(requestBody);
                result.RequestJson = jsonRequest;  // Store for API logging
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                // Send the request to OpenRouter
                HttpResponseMessage response = await httpClient.PostAsync("chat/completions", content);
                stopwatch.Stop();
                result.LatencyMs = stopwatch.Elapsed.TotalMilliseconds;
                result.HttpStatusCode = (int)response.StatusCode;

                // Read the response
                string jsonResponse = await response.Content.ReadAsStringAsync();
                result.ResponseJson = jsonResponse;  // Store for API logging

                // Check if the request was successful
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"API Error: {jsonResponse}");
                    Console.WriteLine("API Error: " + jsonResponse);
                    result.IsError = true;
                    result.ErrorMessage = jsonResponse;
                    result.Direction = "ERROR";
                    return result;
                }

                // Parse the JSON response to get the AI's answer, usage, and actual cost
                var (aiResponse, promptTokens, completionTokens, actualCost) = ParseResponseWithUsage(jsonResponse);

                // Store the raw AI response for display
                result.RawResponse = aiResponse;

                // Extract just the direction letter from the response
                string direction = ExtractDirection(aiResponse);

                result.Direction = direction;
                result.PromptTokens = promptTokens;
                result.CompletionTokens = completionTokens;
                result.TotalTokens = promptTokens + completionTokens;
                result.ActualCostUsd = actualCost;

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.LatencyMs = stopwatch.Elapsed.TotalMilliseconds;
                _logger.LogError("Error calling AI", ex);
                Console.WriteLine("Error calling AI: " + ex.Message);
                result.IsError = true;
                result.ErrorMessage = ex.Message;
                result.Direction = "ERROR";
                return result;
            }
        }

        // Parse the JSON response from OpenRouter to get the message content, usage, and actual cost
        private (string content, int promptTokens, int completionTokens, decimal cost) ParseResponseWithUsage(string jsonResponse)
        {
            string content = "";
            int promptTokens = 0;
            int completionTokens = 0;
            decimal cost = 0m;

            // Use JsonDocument to parse the response
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            JsonElement root = doc.RootElement;

            // Navigate through the JSON structure:
            // { "choices": [ { "message": { "content": "..." } } ], "usage": { "prompt_tokens": N, "completion_tokens": N, "cost": N } }
            if (root.TryGetProperty("choices", out JsonElement choices))
            {
                if (choices.GetArrayLength() > 0)
                {
                    JsonElement firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out JsonElement message))
                    {
                        if (message.TryGetProperty("content", out JsonElement contentElement))
                        {
                            content = contentElement.GetString() ?? "";
                        }
                    }
                }
            }

            // Extract usage information including actual cost from OpenRouter
            if (root.TryGetProperty("usage", out JsonElement usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out JsonElement promptTokensElement))
                {
                    promptTokens = promptTokensElement.GetInt32();
                }
                if (usage.TryGetProperty("completion_tokens", out JsonElement completionTokensElement))
                {
                    completionTokens = completionTokensElement.GetInt32();
                }
                // Extract actual cost as reported by OpenRouter (in credits/USD)
                if (usage.TryGetProperty("cost", out JsonElement costElement))
                {
                    cost = costElement.GetDecimal();
                }
            }

            return (content, promptTokens, completionTokens, cost);
        }

        // Extract direction from AI response using the required "MOVE: X" format
        // Returns the direction letter (N, S, E, W) or "ERROR" if format not found
        private string ExtractDirection(string response)
        {
            // Look for the required format: "MOVE: N" (or S, E, W)
            // The regex is case-insensitive and allows for some whitespace flexibility
            var match = Regex.Match(response, @"MOVE:\s*([NSEW])", RegexOptions.IgnoreCase);

            if (match.Success)
            {
                // Return the captured direction letter in uppercase
                return match.Groups[1].Value.ToUpper();
            }

            // Format not found - log for debugging
            _logger.LogWarning($"Could not find 'MOVE: X' format in AI response: {response.Substring(0, Math.Min(100, response.Length))}...");

            return "ERROR";
        }

        // Build the prompt that describes the current maze situation to the AI
        // This is a self-contained prompt with everything the AI needs
        public static string BuildPrompt(
            int[,] map,
            int row,
            int col,
            bool includeCoordinates,
            bool includeAsciiMap,
            List<GameMoveRecord> moveHistory,
            bool breadcrumbs)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("You are navigating a maze. Your goal is to find the exit.");
            prompt.AppendLine();

            // Optionally include the ASCII map
            if (includeAsciiMap)
            {
                prompt.AppendLine("Here is the current maze (P=You, #=Wall, .=Path, X=Exit):");
                prompt.AppendLine();
                prompt.AppendLine(BuildAsciiMap(map, row, col));
            }

            // Show current position
            if (includeCoordinates)
            {
                prompt.AppendLine($"Your current position: ({col},{row})");
                prompt.AppendLine();
            }

            // Describe what the player can see in each direction
            prompt.AppendLine("From your current position, here is what you see:");
            prompt.AppendLine();

            // Check each direction
            int northCell = map[row - 1, col];
            prompt.AppendLine("  NORTH: " + DescribeCell(northCell));

            int southCell = map[row + 1, col];
            prompt.AppendLine("  SOUTH: " + DescribeCell(southCell));

            int eastCell = map[row, col + 1];
            prompt.AppendLine("  EAST: " + DescribeCell(eastCell));

            int westCell = map[row, col - 1];
            prompt.AppendLine("  WEST: " + DescribeCell(westCell));

            // Include full move history - AI needs all context to make good decisions
            if (moveHistory.Count > 0)
            {
                prompt.AppendLine();

                // Count how many times we've visited each position
                var visitCounts = new Dictionary<string, int>();
                foreach (var move in moveHistory)
                {
                    if (!move.HitWall)
                    {
                        string posKey = $"{move.ToCol},{move.ToRow}";
                        if (visitCounts.ContainsKey(posKey))
                            visitCounts[posKey]++;
                        else
                            visitCounts[posKey] = 1;
                    }
                }

                if (breadcrumbs)
                {
                    prompt.AppendLine("You have been leaving breadcrumbs as you travel.");
                    prompt.AppendLine("Your journey so far:");
                }
                else
                {
                    prompt.AppendLine("Move history:");
                }

                // Display all moves
                foreach (var move in moveHistory)
                {
                    string moveStr = move.ToCompactString();

                    // If breadcrumbs is on, mark revisited spots
                    if (breadcrumbs && !move.HitWall)
                    {
                        string posKey = $"{move.ToCol},{move.ToRow}";
                        if (visitCounts.ContainsKey(posKey) && visitCounts[posKey] > 1)
                        {
                            moveStr += " [crumbs]";
                        }
                    }

                    prompt.AppendLine("  " + moveStr);
                }

                // If breadcrumbs is on and we're revisiting a lot, add a hint
                if (breadcrumbs)
                {
                    string currentPosKey = $"{col},{row}";
                    if (visitCounts.ContainsKey(currentPosKey) && visitCounts[currentPosKey] >= 2)
                    {
                        prompt.AppendLine();
                        prompt.AppendLine("WARNING: Lots of breadcrumbs here - you're going in circles!");
                    }
                }
            }

            // Final instruction - remind about the required format
            prompt.AppendLine();
            prompt.AppendLine("Choose your next move. Remember to end your response with: MOVE: N, S, E, or W");

            return prompt.ToString();
        }

        // Build an ASCII representation of the maze
        private static string BuildAsciiMap(int[,] map, int playerRow, int playerCol)
        {
            StringBuilder sb = new StringBuilder();
            int rows = map.GetLength(0);
            int cols = map.GetLength(1);

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    if (row == playerRow && col == playerCol)
                    {
                        sb.Append("P ");
                    }
                    else
                    {
                        int cell = map[row, col];
                        if (cell == 1)
                            sb.Append("# ");
                        else if (cell == 0)
                            sb.Append(". ");
                        else if (cell == 2)
                            sb.Append("X ");
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // Convert a cell value to a description
        private static string DescribeCell(int cellValue)
        {
            if (cellValue == 0)
            {
                return "Open path (you can walk here)";
            }
            else if (cellValue == 1)
            {
                return "Wall (blocked)";
            }
            else if (cellValue == 2)
            {
                return "THE EXIT! (this is your goal)";
            }
            else
            {
                return "Unknown";
            }
        }
    }
}
