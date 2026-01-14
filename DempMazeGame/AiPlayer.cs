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
        // Top models from OpenRouter by usage
        public static readonly string[] ModelNames =
        {
            // Top 10 most popular on OpenRouter
            "Claude Sonnet 4.5",
            "Grok 4",
            "MiMo-V2-Flash (free)",
            "Gemini 3 Flash Preview",
            "Claude Opus 4.5",
            "Gemini 2.5 Flash",
            "DeepSeek V3.2",
            "Gemini 2.5 Flash Lite",
            "Grok 4.1 Fast",
            "GLM 4.7",
            "Claude Haiku 4.5",
            "GPT-4o Mini",
            "O3 Mini",
            "DeepSeek R1-0528 (free)",
            "Llama 4 Scout",
            "GPT-5.2"
        };

        public static readonly string[] ModelIds =
        {
            "anthropic/claude-sonnet-4.5",
            "x-ai/grok-4",
            "xiaomi/mimo-v2-flash:free",
            "google/gemini-3-flash-preview",
            "anthropic/claude-opus-4.5",
            "google/gemini-2.5-flash",
            "deepseek/deepseek-v3.2",
            "google/gemini-2.5-flash-lite",
            "x-ai/grok-4.1-fast",
            "zhipu/glm-4.7",
            "anthropic/claude-haiku-4.5",
            "openai/gpt-4o-mini",
            "openai/o3-mini",
            "deepseek/deepseek-r1-0528:free",
            "meta-llama/llama-4-scout",
            "openai/gpt-5.2"
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
                    // OpenRouter requires EITHER effort OR max_tokens, never both
                    // Anthropic models use max_tokens, OpenAI/others use effort
                    object? reasoningConfig = null;
                    if (reasoningEnabled)
                    {
                        bool isAnthropicModel = modelId.StartsWith("anthropic/", StringComparison.OrdinalIgnoreCase);

                        if (reasoningMaxTokens.HasValue)
                        {
                            // User specified explicit reasoning budget - use max_tokens
                            reasoningConfig = new
                            {
                                enabled = true,
                                max_tokens = reasoningMaxTokens.Value,
                                exclude = false
                            };
                        }
                        else if (isAnthropicModel)
                        {
                            // Anthropic models require max_tokens instead of effort
                            // Convert effort level to approximate max_tokens value
                            int maxTokensFromEffort = reasoningEffort.ToLower() switch
                            {
                                "xhigh" => 32000,   // 95% of typical Anthropic max
                                "high" => 16000,    // 80%
                                "medium" => 8000,   // 50% (default)
                                "low" => 4000,      // 20%
                                "minimal" => 2000,  // 10%
                                "none" => 1024,     // minimum
                                _ => 8000           // default to medium
                            };

                            reasoningConfig = new
                            {
                                enabled = true,
                                max_tokens = 2000, // just hard code it to test for now
                                exclude = false
                            };
                        }
                        else
                        {
                            // OpenAI, Gemini, and other models use effort
                            reasoningConfig = new
                            {
                                enabled = true,
                                effort = reasoningEffort,
                                exclude = false
                            };
                        }
                    }

                    // Build request with tool calling
                    if (reasoningConfig != null)
                    {
                        requestBody = new
                        {
                            model = modelId,
                            messages = messages,
                            max_tokens = 5000,  // Allow reasoning models to think
                            temperature = 0.1,  // Low temperature = more consistent responses
                            tools = tools,
                            tool_choice = "auto",  // let the model decide when to use the tool
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
                            max_tokens = 5000,
                            temperature = 0.1,
                            tools = tools,
                            tool_choice = "auto",
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
            bool breadcrumbs,
            bool distanceToWall = true,
            bool showGoalCoordinates = true,
            int maxRevisitsPerCell = 10)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("You are navigating a maze. Your goal is to reach the end position.");
            prompt.AppendLine();

            // Coordinate system explanation (if coordinates or goal position shown)
            if (includeCoordinates || showGoalCoordinates)
            {
                prompt.AppendLine("The coordinate system works as follows:");
                prompt.AppendLine("- X-axis (first number): 0 is leftmost, higher values move right (east)");
                prompt.AppendLine("- Y-axis (second number): 0 is topmost, higher values move down (south)");
                prompt.AppendLine("- North decreases Y, south increases Y, east increases X, west decreases X");
                prompt.AppendLine();
            }

            // Optionally include the ASCII map
            if (includeAsciiMap)
            {
                prompt.AppendLine("Here is the current maze (P=You, #=Wall, .=Path, X=Exit):");
                prompt.AppendLine();
                prompt.AppendLine(BuildAsciiMap(map, row, col));
            }

            // Distance to walls in each direction (new feature)
            if (distanceToWall)
            {
                var distances = CalculateDistancesToWalls(map, row, col);
                prompt.AppendLine("You can see how many cells you can move in each direction:");
                prompt.AppendLine($"- North: {distances.North} cells{(distances.North > 0 ? $" (to cell ({col}, {row - distances.North}))" : "")}{(distances.ExitNorth ? " [EXIT VISIBLE!]" : "")}");
                prompt.AppendLine($"- South: {distances.South} cells{(distances.South > 0 ? $" (to cell ({col}, {row + distances.South}))" : "")}{(distances.ExitSouth ? " [EXIT VISIBLE!]" : "")}");
                prompt.AppendLine($"- East: {distances.East} cells{(distances.East > 0 ? $" (to cell ({col + distances.East}, {row}))" : "")}{(distances.ExitEast ? " [EXIT VISIBLE!]" : "")}");
                prompt.AppendLine($"- West: {distances.West} cells{(distances.West > 0 ? $" (to cell ({col - distances.West}, {row}))" : "")}{(distances.ExitWest ? " [EXIT VISIBLE!]" : "")}");

                // Add explicit call-to-action if exit is visible
                if (distances.ExitNorth || distances.ExitSouth || distances.ExitEast || distances.ExitWest)
                {
                    string exitDirection = distances.ExitNorth ? "north" : distances.ExitSouth ? "south" : distances.ExitEast ? "east" : "west";
                    prompt.AppendLine();
                    prompt.AppendLine($"*** THE EXIT IS VISIBLE TO THE {exitDirection.ToUpper()}! Go {exitDirection} to reach it! ***");
                }
                prompt.AppendLine();
            }

            // Show current position and goal (if enabled)
            if (includeCoordinates)
            {
                prompt.AppendLine($"Your current position is ({col}, {row}).");
            }

            if (showGoalCoordinates)
            {
                // Find the goal position in the map
                var goalPos = FindGoalPosition(map);
                if (goalPos.HasValue)
                {
                    prompt.AppendLine($"The goal is at position ({goalPos.Value.col}, {goalPos.Value.row}).");
                }
            }

            if (includeCoordinates || showGoalCoordinates)
            {
                prompt.AppendLine();
            }

            // Include move history with distance observations (condensed format)
            if (moveHistory.Count > 0)
            {
                if (distanceToWall && !breadcrumbs)
                {
                    // New condensed format with distance data
                    prompt.AppendLine("Your travel history (with what you observed at each position):");
                    prompt.AppendLine("Note: Numbers show how many cells you could see in each direction (e.g., 2E means 2 cells east)");

                    for (int i = 0; i < moveHistory.Count; i++)
                    {
                        var move = moveHistory[i];
                        if (!move.HitWall)
                        {
                            // Calculate distances at the FROM position
                            var distances = CalculateDistancesToWalls(map, move.FromRow, move.FromCol);
                            string distanceStr = $"[saw: {distances.East}E, {distances.West}W, {distances.South}S, {distances.North}N]";
                            // Note if exit was visible from that position
                            string exitNote = "";
                            if (distances.ExitNorth) exitNote = " [EXIT was visible N!]";
                            else if (distances.ExitSouth) exitNote = " [EXIT was visible S!]";
                            else if (distances.ExitEast) exitNote = " [EXIT was visible E!]";
                            else if (distances.ExitWest) exitNote = " [EXIT was visible W!]";
                            prompt.AppendLine($"{i + 1}. At ({move.FromCol}, {move.FromRow}) {distanceStr}{exitNote} - Moved {DirectionToWord(move.Direction).ToLower()} to position ({move.ToCol}, {move.ToRow})");
                        }
                        else
                        {
                            prompt.AppendLine($"{i + 1}. At ({move.FromCol}, {move.FromRow}) - Tried to move {DirectionToWord(move.Direction).ToLower()} but hit a wall");
                        }
                    }
                }
                else if (breadcrumbs)
                {
                    // Breadcrumbs mode - count visits
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

                    prompt.AppendLine("You have been leaving breadcrumbs as you travel.");
                    prompt.AppendLine("Your journey so far:");

                    foreach (var move in moveHistory)
                    {
                        string moveStr = move.ToCompactString();

                        // Mark revisited spots
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

                    // Warn about circles
                    string currentPosKey = $"{col},{row}";
                    if (visitCounts.ContainsKey(currentPosKey) && visitCounts[currentPosKey] >= 2)
                    {
                        prompt.AppendLine();
                        prompt.AppendLine("WARNING: Lots of breadcrumbs here - you're going in circles!");
                    }
                }
                else
                {
                    // Simple move history
                    prompt.AppendLine("Move history:");
                    foreach (var move in moveHistory)
                    {
                        prompt.AppendLine("  " + move.ToCompactString());
                    }
                }

                prompt.AppendLine();
            }

            // Final instruction
            prompt.AppendLine("Use the move function to navigate. Choose a direction: north, south, east, or west.");

            // Warning about revisit limit
            if (maxRevisitsPerCell > 0)
            {
                prompt.AppendLine();
                prompt.AppendLine($"WARNING: If you visit the same cell {maxRevisitsPerCell} times, the evaluation will be terminated. Avoid getting stuck in loops!");
            }

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

        // Calculate how many cells can be seen in each direction until hitting a wall
        // Also returns whether the exit is visible in each direction
        private static (int North, int South, int East, int West, bool ExitNorth, bool ExitSouth, bool ExitEast, bool ExitWest) CalculateDistancesToWalls(int[,] map, int row, int col)
        {
            int north = 0, south = 0, east = 0, west = 0;
            bool exitNorth = false, exitSouth = false, exitEast = false, exitWest = false;

            // North - decrease row
            for (int r = row - 1; r >= 0; r--)
            {
                if (map[r, col] == 1) break;  // Hit a wall
                north++;
                if (map[r, col] == 2) exitNorth = true;  // Found the exit
            }

            // South - increase row
            for (int r = row + 1; r < map.GetLength(0); r++)
            {
                if (map[r, col] == 1) break;  // Hit a wall
                south++;
                if (map[r, col] == 2) exitSouth = true;  // Found the exit
            }

            // East - increase col
            for (int c = col + 1; c < map.GetLength(1); c++)
            {
                if (map[row, c] == 1) break;  // Hit a wall
                east++;
                if (map[row, c] == 2) exitEast = true;  // Found the exit
            }

            // West - decrease col
            for (int c = col - 1; c >= 0; c--)
            {
                if (map[row, c] == 1) break;  // Hit a wall
                west++;
                if (map[row, c] == 2) exitWest = true;  // Found the exit
            }

            return (north, south, east, west, exitNorth, exitSouth, exitEast, exitWest);
        }

        // Find the goal position (cell with value 2) in the map
        private static (int row, int col)? FindGoalPosition(int[,] map)
        {
            int rows = map.GetLength(0);
            int cols = map.GetLength(1);

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    if (map[row, col] == 2)
                    {
                        return (row, col);
                    }
                }
            }

            return null;  // No goal found
        }

        // Convert direction letter to full word
        private static string DirectionToWord(string direction)
        {
            return direction.ToUpper() switch
            {
                "N" => "North",
                "S" => "South",
                "E" => "East",
                "W" => "West",
                _ => direction
            };
        }
    }
}
