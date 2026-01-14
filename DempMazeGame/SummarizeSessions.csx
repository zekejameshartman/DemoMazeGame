#!/usr/bin/env dotnet-script
// Run with: dotnet script SummarizeSessions.csx
// Or if you don't have dotnet-script: dotnet run SummarizeSessions.cs

#r "nuget: System.Text.Json, 8.0.0"

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;

// Simple session data extractor for science fair analysis
var sessionsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs", "sessions");

if (!Directory.Exists(sessionsDir))
{
    Console.WriteLine($"Sessions directory not found: {sessionsDir}");
    return;
}

// Get all session files (excluding _api.json files)
var sessionFiles = Directory.GetFiles(sessionsDir, "*.json")
    .Where(f => !f.EndsWith("_api.json"))
    .OrderBy(f => f)
    .ToList();

Console.WriteLine($"Found {sessionFiles.Count} session files\n");

// CSV header
var csvLines = new List<string>
{
    "Timestamp,Model,GoalCoordsOn,Won,StoppedByUser,MaxMoves,TooManyRevisits,Error,TotalMoves,SuccessfulMoves,WallCollisions,UniquePositions,Backtracks,BacktrackRate,TotalTokens,CostUSD"
};

// Summary stats
int totalSessions = 0;
int wins = 0;
int goalOnCount = 0;
int goalOffCount = 0;
int goalOnWins = 0;
int goalOffWins = 0;
var modelStats = new Dictionary<string, (int runs, int wins, int totalMoves, int backtracks, int collisions)>();

foreach (var file in sessionFiles)
{
    try
    {
        var json = File.ReadAllText(file);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Extract data
        var timestamp = Path.GetFileName(file).Substring(0, 19); // yyyy-MM-dd_HHmmss
        var model = root.GetProperty("model").GetProperty("name").GetString() ?? "Unknown";
        var settings = root.GetProperty("settings");
        var goalCoordsOn = settings.GetProperty("showGoalCoordinates").GetBoolean();
        var outcome = root.GetProperty("outcome");
        var won = outcome.GetProperty("won").GetBoolean();
        var stoppedByUser = outcome.GetProperty("stoppedByUser").GetBoolean();
        var maxMoves = outcome.GetProperty("reachedMaxMoves").GetBoolean();
        var tooManyRevisits = outcome.GetProperty("tooManyRevisits").GetBoolean();
        var error = outcome.GetProperty("errorOccurred").GetBoolean();
        var metrics = root.GetProperty("metrics");
        var totalMoves = metrics.GetProperty("totalMoves").GetInt32();
        var successfulMoves = metrics.GetProperty("successfulMoves").GetInt32();
        var wallCollisions = metrics.GetProperty("wallCollisions").GetInt32();
        var uniquePositions = metrics.GetProperty("uniquePositionsVisited").GetInt32();
        var backtracks = metrics.GetProperty("backtrackCount").GetInt32();
        var tokens = root.GetProperty("tokenUsage").GetProperty("totalTokens").GetInt32();
        var cost = root.GetProperty("cost").GetProperty("totalCostUsd").GetDecimal();

        var backtrackRate = totalMoves > 0 ? (double)backtracks / totalMoves * 100 : 0;

        // CSV line
        csvLines.Add($"{timestamp},{model},{goalCoordsOn},{won},{stoppedByUser},{maxMoves},{tooManyRevisits},{error},{totalMoves},{successfulMoves},{wallCollisions},{uniquePositions},{backtracks},{backtrackRate:F1},{tokens},{cost:F4}");

        // Update stats
        totalSessions++;
        if (won) wins++;

        if (goalCoordsOn)
        {
            goalOnCount++;
            if (won) goalOnWins++;
        }
        else
        {
            goalOffCount++;
            if (won) goalOffWins++;
        }

        // Model stats
        if (!modelStats.ContainsKey(model))
            modelStats[model] = (0, 0, 0, 0, 0);
        var (runs, mwins, mtotal, mback, mcoll) = modelStats[model];
        modelStats[model] = (runs + 1, mwins + (won ? 1 : 0), mtotal + totalMoves, mback + backtracks, mcoll + wallCollisions);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error parsing {Path.GetFileName(file)}: {ex.Message}");
    }
}

// Write CSV
var csvPath = Path.Combine(Directory.GetCurrentDirectory(), "session_summary.csv");
File.WriteAllLines(csvPath, csvLines);
Console.WriteLine($"CSV written to: {csvPath}\n");

// Print summary report
Console.WriteLine("=" + new string('=', 60));
Console.WriteLine("SCIENCE FAIR DATA SUMMARY");
Console.WriteLine("=" + new string('=', 60));
Console.WriteLine();
Console.WriteLine($"Total Sessions: {totalSessions}");
Console.WriteLine($"Total Wins: {wins} ({(totalSessions > 0 ? (double)wins / totalSessions * 100 : 0):F1}%)");
Console.WriteLine();

Console.WriteLine("--- KEY COMPARISON: Goal Coordinates ON vs OFF ---");
Console.WriteLine();
Console.WriteLine($"  Goal Coords ON:  {goalOnCount} runs, {goalOnWins} wins ({(goalOnCount > 0 ? (double)goalOnWins / goalOnCount * 100 : 0):F1}% success)");
Console.WriteLine($"  Goal Coords OFF: {goalOffCount} runs, {goalOffWins} wins ({(goalOffCount > 0 ? (double)goalOffWins / goalOffCount * 100 : 0):F1}% success)");
Console.WriteLine();

if (goalOffCount == 0)
{
    Console.WriteLine("  ⚠️  NO DATA with Goal Coordinates OFF!");
    Console.WriteLine("  To test the hypothesis, run sessions with ShowGoalCoordinates = false");
}
Console.WriteLine();

Console.WriteLine("--- BY MODEL ---");
Console.WriteLine();
Console.WriteLine($"{"Model",-30} {"Runs",6} {"Wins",6} {"Win%",7} {"Avg Moves",10} {"Backtrack%",11} {"Collisions",11}");
Console.WriteLine(new string('-', 85));

foreach (var (model, stats) in modelStats.OrderByDescending(x => x.Value.runs))
{
    var avgMoves = stats.runs > 0 ? (double)stats.totalMoves / stats.runs : 0;
    var backtrackPct = stats.totalMoves > 0 ? (double)stats.backtracks / stats.totalMoves * 100 : 0;
    var winPct = stats.runs > 0 ? (double)stats.wins / stats.runs * 100 : 0;
    Console.WriteLine($"{model,-30} {stats.runs,6} {stats.wins,6} {winPct,6:F1}% {avgMoves,10:F1} {backtrackPct,10:F1}% {stats.collisions,11}");
}

Console.WriteLine();
Console.WriteLine("=" + new string('=', 60));
Console.WriteLine();
Console.WriteLine("Next steps:");
Console.WriteLine("1. Open session_summary.csv in Excel/Google Sheets");
Console.WriteLine("2. Create charts comparing Goal Coords ON vs OFF");
Console.WriteLine("3. Run more sessions with ShowGoalCoordinates = false");
