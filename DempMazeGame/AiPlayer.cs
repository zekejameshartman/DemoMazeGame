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
        public async Task<AiMoveResult> GetAiMove(string prompt, string modelId, bool reasoningEnabled = true, string reasoningEffort = "medium", int? reasoningMaxTokens = null)
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

                // Try tool calling first, with JSON-mode fallback
                // Most modern models (Claude, GPT, Gemini) support tool calling
                bool usingToolCalling = true;
                object requestBody;

                try
                {
                    // Define the "move" tool with direction enum parameter
                    var tools = new object[]
                    {
                        new
                        {
                            type = "function",
                            function = new
                            {
                                name = "move",
                                description = "Make a move in the specified direction",
                                parameters = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        direction = new
                                        {
                                            type = "string",
                                            @enum = new[] { "north", "south", "east", "west" },
                                            description = "The direction to move"
                                        }
                                    },
                                    required = new[] { "direction" }
                                }
                            }
                        }
                    };

                    // Build reasoning configuration if enabled
                    object? reasoningConfig = null;
                    if (reasoningEnabled)
                    {
                        if (reasoningMaxTokens.HasValue)
                        {
                            reasoningConfig = new { max_tokens = reasoningMaxTokens.Value };
                        }
                        else
                        {
                            reasoningConfig = new { effort = reasoningEffort };
                        }
                    }

                    // Build request with tool calling
                    if (reasoningConfig != null)
                    {
                        requestBody = new
                        {
                            model = modelId,
                            messages = messages,
                            max_tokens = 500,  // Allow reasoning models to think
                            temperature = 0.1,  // Low temperature = more consistent responses
                            tools = tools,
                            tool_choice = new { type = "function", function = new { name = "move" } },  // Force tool call
                            reasoning = reasoningConfig,  // Add reasoning configuration
                            usage = new { include = true }  // Request actual cost from OpenRouter
                        };
                    }
                    else
                    {
                        requestBody = new
                        {
                            model = modelId,
                            messages = messages,
                            max_tokens = 500,
                            temperature = 0.1,
                            tools = tools,
                            tool_choice = new { type = "function", function = new { name = "move" } },
                            usage = new { include = true }
                        };
                    }
                }
                catch
                {
                    // If tool construction fails, fall back to JSON mode
                    usingToolCalling = false;
                    requestBody = new
                    {
                        model = modelId,
                        messages = messages,
                        max_tokens = 500,
                        temperature = 0.1,
                        response_format = new { type = "json_object" },
                        usage = new { include = true }
                    };
                }

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
                    // If tool calling failed, try JSON mode fallback
                    if (usingToolCalling && jsonResponse.Contains("tool"))
                    {
                        _logger.LogInfo($"Tool calling not supported by {modelId}, falling back to JSON mode");
                        return await GetAiMoveJsonMode(prompt, modelId);
                    }

                    _logger.LogError($"API Error: {jsonResponse}");
                    Console.WriteLine("API Error: " + jsonResponse);
                    result.IsError = true;
                    result.ErrorMessage = jsonResponse;
                    result.Direction = "ERROR";
                    return result;
                }

                // Parse the response based on mode
                string direction;
                if (usingToolCalling)
                {
                    var (aiResponse, dir, promptTokens, completionTokens, actualCost, reasoning, reasoningTokens) = ParseToolCallResponse(jsonResponse);
                    result.RawResponse = aiResponse;
                    direction = dir;
                    result.PromptTokens = promptTokens;
                    result.CompletionTokens = completionTokens;
                    result.TotalTokens = promptTokens + completionTokens;
                    result.ActualCostUsd = actualCost;
                    result.Reasoning = reasoning;
                    result.ReasoningTokens = reasoningTokens;
                }
                else
                {
                    var (aiResponse, dir, promptTokens, completionTokens, actualCost, reasoning, reasoningTokens) = ParseJsonModeResponse(jsonResponse);
                    result.RawResponse = aiResponse;
                    direction = dir;
                    result.PromptTokens = promptTokens;
                    result.CompletionTokens = completionTokens;
                    result.TotalTokens = promptTokens + completionTokens;
                    result.ActualCostUsd = actualCost;
                    result.Reasoning = reasoning;
                    result.ReasoningTokens = reasoningTokens;
                }

                result.Direction = direction;
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

        // JSON mode fallback when tool calling is not supported
        private async Task<AiMoveResult> GetAiMoveJsonMode(string prompt, string modelId)
        {
            var result = new AiMoveResult();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Build the messages array with system prompt + user message
                // Modify system prompt to request JSON output
                string jsonSystemPrompt = _systemPrompt + "\n\nRespond with JSON in this format: {\"direction\": \"north|south|east|west\"}";
                var messages = new object[]
                {
                    new { role = "system", content = jsonSystemPrompt },
                    new { role = "user", content = prompt }
                };

                var requestBody = new
                {
                    model = modelId,
                    messages = messages,
                    max_tokens = 500,
                    temperature = 0.1,
                    response_format = new { type = "json_object" },
                    usage = new { include = true }
                };

                string jsonRequest = JsonSerializer.Serialize(requestBody);
                result.RequestJson = jsonRequest;
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await httpClient.PostAsync("chat/completions", content);
                stopwatch.Stop();
                result.LatencyMs = stopwatch.Elapsed.TotalMilliseconds;
                result.HttpStatusCode = (int)response.StatusCode;

                string jsonResponse = await response.Content.ReadAsStringAsync();
                result.ResponseJson = jsonResponse;

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"API Error: {jsonResponse}");
                    Console.WriteLine("API Error: " + jsonResponse);
                    result.IsError = true;
                    result.ErrorMessage = jsonResponse;
                    result.Direction = "ERROR";
                    return result;
                }

                var (aiResponse, direction, promptTokens, completionTokens, actualCost, reasoning, reasoningTokens) = ParseJsonModeResponse(jsonResponse);
                result.RawResponse = aiResponse;
                result.Direction = direction;
                result.PromptTokens = promptTokens;
                result.CompletionTokens = completionTokens;
                result.TotalTokens = promptTokens + completionTokens;
                result.ActualCostUsd = actualCost;
                result.Reasoning = reasoning;
                result.ReasoningTokens = reasoningTokens;

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.LatencyMs = stopwatch.Elapsed.TotalMilliseconds;
                _logger.LogError("Error calling AI in JSON mode", ex);
                Console.WriteLine("Error calling AI: " + ex.Message);
                result.IsError = true;
                result.ErrorMessage = ex.Message;
                result.Direction = "ERROR";
                return result;
            }
        }

        // Parse tool call response - extracts reasoning from content and direction from tool_calls
        private (string content, string direction, int promptTokens, int completionTokens, decimal cost, string? reasoning, int reasoningTokens) ParseToolCallResponse(string jsonResponse)
        {
            string content = "";
            string direction = "ERROR";
            int promptTokens = 0;
            int completionTokens = 0;
            decimal cost = 0m;
            string? reasoning = null;
            int reasoningTokens = 0;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                JsonElement root = doc.RootElement;

                // Navigate: { "choices": [ { "message": { "content": "...", "tool_calls": [...] } } ] }
                if (root.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                {
                    JsonElement firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out JsonElement message))
                    {
                        // Get reasoning content (chain-of-thought)
                        if (message.TryGetProperty("content", out JsonElement contentElement))
                        {
                            content = contentElement.GetString() ?? "";
                        }

                        // Extract reasoning field if present
                        if (message.TryGetProperty("reasoning", out JsonElement reasoningElement))
                        {
                            reasoning = reasoningElement.GetString();
                        }

                        // Extract direction from tool_calls
                        if (message.TryGetProperty("tool_calls", out JsonElement toolCalls) && toolCalls.GetArrayLength() > 0)
                        {
                            JsonElement firstToolCall = toolCalls[0];
                            if (firstToolCall.TryGetProperty("function", out JsonElement function))
                            {
                                if (function.TryGetProperty("arguments", out JsonElement arguments))
                                {
                                    string argsJson = arguments.GetString() ?? "{}";
                                    using JsonDocument argsDoc = JsonDocument.Parse(argsJson);
                                    if (argsDoc.RootElement.TryGetProperty("direction", out JsonElement dirElement))
                                    {
                                        string dir = dirElement.GetString() ?? "";
                                        direction = ConvertDirectionToLetter(dir);
                                    }
                                }
                            }
                        }
                    }
                }

                // Extract usage information including reasoning_tokens
                if (root.TryGetProperty("usage", out JsonElement usage))
                {
                    if (usage.TryGetProperty("prompt_tokens", out JsonElement promptTokensElement))
                        promptTokens = promptTokensElement.GetInt32();
                    if (usage.TryGetProperty("completion_tokens", out JsonElement completionTokensElement))
                        completionTokens = completionTokensElement.GetInt32();
                    if (usage.TryGetProperty("cost", out JsonElement costElement))
                        cost = costElement.GetDecimal();

                    // Extract reasoning_tokens from completion_tokens_details
                    if (usage.TryGetProperty("completion_tokens_details", out JsonElement completionDetails))
                    {
                        if (completionDetails.TryGetProperty("reasoning_tokens", out JsonElement reasoningTokensElement))
                        {
                            reasoningTokens = reasoningTokensElement.GetInt32();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error parsing tool call response", ex);
            }

            return (content, direction, promptTokens, completionTokens, cost, reasoning, reasoningTokens);
        }

        // Parse JSON mode response - extracts direction from JSON object in content
        private (string content, string direction, int promptTokens, int completionTokens, decimal cost, string? reasoning, int reasoningTokens) ParseJsonModeResponse(string jsonResponse)
        {
            string content = "";
            string direction = "ERROR";
            int promptTokens = 0;
            int completionTokens = 0;
            decimal cost = 0m;
            string? reasoning = null;
            int reasoningTokens = 0;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                JsonElement root = doc.RootElement;

                // Navigate: { "choices": [ { "message": { "content": "{\"direction\": \"north\"}" } } ] }
                if (root.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                {
                    JsonElement firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out JsonElement message))
                    {
                        if (message.TryGetProperty("content", out JsonElement contentElement))
                        {
                            content = contentElement.GetString() ?? "";

                            // Parse the JSON content to extract direction
                            try
                            {
                                using JsonDocument contentDoc = JsonDocument.Parse(content);
                                if (contentDoc.RootElement.TryGetProperty("direction", out JsonElement dirElement))
                                {
                                    string dir = dirElement.GetString() ?? "";
                                    direction = ConvertDirectionToLetter(dir);
                                }
                            }
                            catch
                            {
                                _logger.LogWarning($"Could not parse JSON from content: {content.Substring(0, Math.Min(100, content.Length))}");
                            }
                        }

                        // Extract reasoning field if present
                        if (message.TryGetProperty("reasoning", out JsonElement reasoningElement))
                        {
                            reasoning = reasoningElement.GetString();
                        }
                    }
                }

                // Extract usage information including reasoning_tokens
                if (root.TryGetProperty("usage", out JsonElement usage))
                {
                    if (usage.TryGetProperty("prompt_tokens", out JsonElement promptTokensElement))
                        promptTokens = promptTokensElement.GetInt32();
                    if (usage.TryGetProperty("completion_tokens", out JsonElement completionTokensElement))
                        completionTokens = completionTokensElement.GetInt32();
                    if (usage.TryGetProperty("cost", out JsonElement costElement))
                        cost = costElement.GetDecimal();

                    // Extract reasoning_tokens from completion_tokens_details
                    if (usage.TryGetProperty("completion_tokens_details", out JsonElement completionDetails))
                    {
                        if (completionDetails.TryGetProperty("reasoning_tokens", out JsonElement reasoningTokensElement))
                        {
                            reasoningTokens = reasoningTokensElement.GetInt32();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error parsing JSON mode response", ex);
            }

            return (content, direction, promptTokens, completionTokens, cost, reasoning, reasoningTokens);
        }

        // Convert direction string (north/south/east/west) to letter (N/S/E/W)
        private string ConvertDirectionToLetter(string direction)
        {
            return direction.ToLower() switch
            {
                "north" => "N",
                "south" => "S",
                "east" => "E",
                "west" => "W",
                _ => "ERROR"
            };
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

            // Final instruction - remind about tool calling
            prompt.AppendLine();
            prompt.AppendLine("Choose your next move and call the 'move' tool with your chosen direction (north, south, east, or west).");

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
