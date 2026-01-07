using System.Text.Json;
using System.Text.Json.Serialization;
using DemoMazeGame.Models;

namespace DemoMazeGame.Services
{
    // Logs AI session data to individual JSON files per game
    public class AiSessionLogger : IAiSessionLogger
    {
        private readonly IAppLogger _appLogger;
        private readonly string _sessionsDir;
        private AiSessionLog? _currentSession;
        private ApiSessionLog? _currentApiLog;
        private HashSet<(int row, int col)> _visitedPositions = new();
        private (int row, int col)? _previousPosition;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public AiSessionLogger(IAppLogger appLogger)
        {
            _appLogger = appLogger;

            // Session logs in logs/sessions folder
            _sessionsDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "logs", "sessions");
            Directory.CreateDirectory(_sessionsDir);
        }

        public void StartSession(string modelId, string modelName, bool showCoordinates, bool showAsciiMap, int delayBetweenMoves, bool distanceToWall, bool showGoalCoordinates, int maxRevisitsPerCell, int maxMoves, bool reasoningEnabled = true, string reasoningEffort = "medium", int? reasoningMaxTokens = null)
        {
            _currentSession = new AiSessionLog
            {
                StartTime = DateTime.UtcNow,
                Model = new ModelInfo { Id = modelId, Name = modelName },
                Settings = new SessionSettings
                {
                    ShowCoordinates = showCoordinates,
                    ShowAsciiMap = showAsciiMap,
                    DelayBetweenMoves = delayBetweenMoves,
                    DistanceToWall = distanceToWall,
                    ShowGoalCoordinates = showGoalCoordinates,
                    MaxRevisitsPerCell = maxRevisitsPerCell,
                    MaxMoves = maxMoves,
                    ReasoningEnabled = reasoningEnabled,
                    ReasoningEffort = reasoningEffort,
                    ReasoningMaxTokens = reasoningMaxTokens
                }
            };

            // Initialize paired API log
            _currentApiLog = new ApiSessionLog
            {
                StartTime = DateTime.UtcNow,
                ModelId = modelId,
                ModelName = modelName
            };

            _visitedPositions.Clear();
            _previousPosition = null;

            _appLogger.LogInfo($"AI session started: {modelName}");
        }

        public void LogMove(int moveNumber, string direction, int fromRow, int fromCol, int toRow, int toCol, bool wasSuccessful, int promptTokens, int completionTokens, decimal costUsd, int reasoningTokens = 0, string? reasoning = null)
        {
            if (_currentSession == null) return;

            var move = new MoveRecord
            {
                MoveNumber = moveNumber,
                Direction = direction,
                FromPosition = new Position { Row = fromRow, Col = fromCol },
                ToPosition = new Position { Row = toRow, Col = toCol },
                WasSuccessful = wasSuccessful,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                ReasoningTokens = reasoningTokens,
                CostUsd = costUsd,
                Reasoning = reasoning
            };

            _currentSession.Moves.Add(move);

            // Update metrics
            _currentSession.Metrics.TotalMoves++;
            if (wasSuccessful)
            {
                _currentSession.Metrics.SuccessfulMoves++;

                // Track unique positions
                var newPos = (toRow, toCol);
                if (!_visitedPositions.Contains(newPos))
                {
                    _visitedPositions.Add(newPos);
                }
                else
                {
                    // Returning to a visited position counts as backtracking
                    _currentSession.Metrics.BacktrackCount++;
                }

                _previousPosition = newPos;
            }
            else
            {
                _currentSession.Metrics.WallCollisions++;
            }

            // Update token totals
            _currentSession.TokenUsage.TotalPromptTokens += promptTokens;
            _currentSession.TokenUsage.TotalCompletionTokens += completionTokens;
            _currentSession.TokenUsage.TotalTokens += promptTokens + completionTokens;
            _currentSession.TokenUsage.TotalReasoningTokens += reasoningTokens;

            // Update cost
            _currentSession.Cost.TotalCostUsd += costUsd;
        }

        public void LogApiCall(int moveNumber, string requestJson, string responseJson, int httpStatusCode, double latencyMs)
        {
            if (_currentApiLog == null) return;

            // Parse JSON strings into objects for readable logging
            object? requestObj = null;
            object? responseObj = null;

            try
            {
                requestObj = JsonSerializer.Deserialize<object>(requestJson);
            }
            catch
            {
                requestObj = requestJson; // Fallback to raw string if parse fails
            }

            try
            {
                responseObj = JsonSerializer.Deserialize<object>(responseJson);
            }
            catch
            {
                responseObj = responseJson; // Fallback to raw string if parse fails
            }

            _currentApiLog.Calls.Add(new ApiCallEntry
            {
                MoveNumber = moveNumber,
                Timestamp = DateTime.UtcNow,
                Request = requestObj,
                Response = responseObj,
                HttpStatusCode = httpStatusCode,
                LatencyMs = latencyMs
            });
        }

        public void EndSession(bool won, bool stoppedByUser, bool reachedMaxMoves, bool tooManyRevisits, bool errorOccurred, string? errorMessage = null)
        {
            if (_currentSession == null) return;

            _currentSession.EndTime = DateTime.UtcNow;
            _currentSession.DurationSeconds = (_currentSession.EndTime.Value - _currentSession.StartTime).TotalSeconds;

            _currentSession.Outcome = new SessionOutcome
            {
                Won = won,
                StoppedByUser = stoppedByUser,
                ReachedMaxMoves = reachedMaxMoves,
                TooManyRevisits = tooManyRevisits,
                ErrorOccurred = errorOccurred,
                ErrorMessage = errorMessage
            };

            _currentSession.Metrics.UniquePositionsVisited = _visitedPositions.Count;

            // Calculate cost per move
            if (_currentSession.Metrics.TotalMoves > 0)
            {
                _currentSession.Cost.CostPerMove = _currentSession.Cost.TotalCostUsd / _currentSession.Metrics.TotalMoves;
            }

            // Save both log files
            SaveSession();
            SaveApiLog();

            string outcome = won ? "WON" : stoppedByUser ? "STOPPED" : reachedMaxMoves ? "MAX_MOVES" : tooManyRevisits ? "TOO_MANY_REVISITS" : errorOccurred ? "ERROR" : "UNKNOWN";
            _appLogger.LogInfo($"AI session ended: {_currentSession.Model.Name} - {outcome} in {_currentSession.Metrics.TotalMoves} moves");

            _currentSession = null;
            _currentApiLog = null;
        }

        private void SaveSession()
        {
            if (_currentSession == null) return;

            try
            {
                // Filename: timestamp_modelname.json
                string timestamp = _currentSession.StartTime.ToString("yyyy-MM-dd_HHmmss");
                string safeModelName = _currentSession.Model.Name.Replace(" ", "-").Replace("/", "-").Replace(":", "").ToLower();
                string fileName = $"{timestamp}_{safeModelName}.json";
                string filePath = Path.Combine(_sessionsDir, fileName);

                string json = JsonSerializer.Serialize(_currentSession, _jsonOptions);
                File.WriteAllText(filePath, json);

                _appLogger.LogInfo($"Session log saved: {fileName}");
            }
            catch (Exception ex)
            {
                _appLogger.LogError("Failed to save session log", ex);
            }
        }

        private void SaveApiLog()
        {
            if (_currentApiLog == null || _currentApiLog.Calls.Count == 0) return;

            try
            {
                // Filename: timestamp_modelname_api.json (paired with session log)
                string timestamp = _currentApiLog.StartTime.ToString("yyyy-MM-dd_HHmmss");
                string safeModelName = _currentApiLog.ModelName.Replace(" ", "-").Replace("/", "-").Replace(":", "").ToLower();
                string fileName = $"{timestamp}_{safeModelName}_api.json";
                string filePath = Path.Combine(_sessionsDir, fileName);

                string json = JsonSerializer.Serialize(_currentApiLog, _jsonOptions);
                File.WriteAllText(filePath, json);

                _appLogger.LogInfo($"API log saved: {fileName}");
            }
            catch (Exception ex)
            {
                _appLogger.LogError("Failed to save API log", ex);
            }
        }
    }
}
