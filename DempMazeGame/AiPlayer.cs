using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

        // Conversation history - keeps track of all messages sent and received
        private List<ChatMessage> conversationHistory = new List<ChatMessage>();

        // Simple class to hold a chat message
        private class ChatMessage
        {
            public string Role { get; set; } = "";
            public string Content { get; set; } = "";
        }



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

            // Create the HTTP client and set up the headers
            httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/demo-maze-game");
            httpClient.DefaultRequestHeaders.Add("X-Title", "Demo Maze Game");
        }

        // Reset the conversation history (call this at the start of each new game)
        public void ResetConversation()
        {
            conversationHistory.Clear();
        }

        // This method asks the AI which direction to move
        // Returns AiMoveResult with direction and token/cost metrics
        public async Task<AiMoveResult> GetAiMove(string prompt, string modelId)
        {
            var result = new AiMoveResult();

            try
            {
                // Add the user's prompt to the conversation history
                conversationHistory.Add(new ChatMessage { Role = "user", Content = prompt });

                // Build the messages array from conversation history
                var messages = conversationHistory.Select(m => new { role = m.Role, content = m.Content }).ToArray();

                // Build the request body as a JSON object
                // OpenRouter uses the same format as OpenAI's chat API
                // We include usage.include = true to get actual cost from OpenRouter
                var requestBody = new
                {
                    model = modelId,
                    messages = messages,
                    max_tokens = 50,  // We only need a short response
                    temperature = 0.1,  // Low temperature = more consistent responses
                    usage = new { include = true }  // Request actual cost from OpenRouter
                };

                // Convert the request to JSON
                string jsonRequest = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                // Send the request to OpenRouter
                HttpResponseMessage response = await httpClient.PostAsync("chat/completions", content);

                // Read the response
                string jsonResponse = await response.Content.ReadAsStringAsync();

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

                // Add the assistant's response to the conversation history
                conversationHistory.Add(new ChatMessage { Role = "assistant", Content = aiResponse });

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

        // Look through the AI's response and find a direction letter
        // The AI might respond with "N" or "I'll go North" or "N - going north"
        // We need to find just the direction letter
        private string ExtractDirection(string response)
        {
            // Convert to uppercase for easier checking
            string upper = response.ToUpper();

            // First, check if the response starts with a direction
            // This handles responses like "N" or "N - I think north is best"
            if (upper.Length > 0)
            {
                char firstChar = upper[0];
                if (firstChar == 'N' || firstChar == 'S' || firstChar == 'E' || firstChar == 'W')
                {
                    return firstChar.ToString();
                }
            }

            // If not at the start, look for direction words
            if (upper.Contains("NORTH"))
            {
                return "N";
            }
            if (upper.Contains("SOUTH"))
            {
                return "S";
            }
            if (upper.Contains("EAST"))
            {
                return "E";
            }
            if (upper.Contains("WEST"))
            {
                return "W";
            }

            // Last resort: find any N, S, E, or W in the response
            for (int i = 0; i < upper.Length; i++)
            {
                char c = upper[i];
                if (c == 'N' || c == 'S' || c == 'E' || c == 'W')
                {
                    return c.ToString();
                }
            }

            // Couldn't find a direction
            return "ERROR";
        }

        // Build the prompt that describes the current maze situation to the AI
        public static string BuildPrompt(int[,] map, int row, int col, bool includeCoordinates, bool includeAsciiMap)
        {
            // Start with basic instructions
            StringBuilder prompt = new StringBuilder();
            prompt.AppendLine("You are navigating a maze. Your goal is to find the exit.");
            prompt.AppendLine();

            // Optionally include the ASCII map
            if (includeAsciiMap)
            {
                prompt.AppendLine("Here is the current maze (P=You, #=Wall, .=Path, X=Exit):");
                prompt.AppendLine();
                prompt.AppendLine(BuildAsciiMap(map, row, col));
            }

            // Describe what the player can see in each direction
            prompt.AppendLine("From your current position, here is what you see:");
            prompt.AppendLine();

            // Check North
            int northCell = map[row - 1, col];
            prompt.Append("  NORTH: ");
            prompt.AppendLine(DescribeCell(northCell));

            // Check South
            int southCell = map[row + 1, col];
            prompt.Append("  SOUTH: ");
            prompt.AppendLine(DescribeCell(southCell));

            // Check East
            int eastCell = map[row, col + 1];
            prompt.Append("  EAST: ");
            prompt.AppendLine(DescribeCell(eastCell));

            // Check West
            int westCell = map[row, col - 1];
            prompt.Append("  WEST: ");
            prompt.AppendLine(DescribeCell(westCell));

            // Optionally include coordinates (for testing if it helps the AI)
            if (includeCoordinates)
            {
                prompt.AppendLine();
                prompt.AppendLine("Your current coordinates are: (" + col + ", " + row + ")");
            }

            // Final instruction
            prompt.AppendLine();
            prompt.AppendLine("Respond with ONLY a single letter: N, S, E, or W");

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
