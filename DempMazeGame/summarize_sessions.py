#!/usr/bin/env python3
"""
Science Fair Data Summary Script
Reads all AI maze session logs and generates summary statistics + CSV

Run from DempMazeGame directory:
    python3 summarize_sessions.py
"""

import json
import os
from pathlib import Path
from collections import defaultdict

def main():
    sessions_dir = Path("logs/sessions")

    if not sessions_dir.exists():
        print(f"Sessions directory not found: {sessions_dir}")
        return

    # Get all session files (excluding _api.json files)
    session_files = sorted([
        f for f in sessions_dir.glob("*.json")
        if not f.name.endswith("_api.json")
    ])

    print(f"Found {len(session_files)} session files\n")

    # Data collection
    all_sessions = []
    model_stats = defaultdict(lambda: {"runs": 0, "wins": 0, "moves": 0, "backtracks": 0, "collisions": 0})
    goal_on = {"runs": 0, "wins": 0}
    goal_off = {"runs": 0, "wins": 0}
    goal_unknown = {"runs": 0, "wins": 0}  # older sessions without this setting

    for filepath in session_files:
        try:
            with open(filepath) as f:
                data = json.load(f)

            # Handle older sessions that may not have all settings/fields
            settings = data.get("settings", {})
            outcome = data.get("outcome", {})
            metrics = data.get("metrics", {})
            tokens = data.get("tokenUsage", {})
            cost = data.get("cost", {})

            # Extract timestamp - handle both old format (yyyy-MM-dd_HHmmss) and new format (yyyy-MM-dd_HHmmss_fff_xxxx)
            # Old: 2026-01-13_021011_model.json (19 chars before model)
            # New: 2026-01-13_021011_123_a3f0_model.json (has ms and session id)
            filename_parts = filepath.name.split('_')
            timestamp = f"{filename_parts[0]}_{filename_parts[1]}" if len(filename_parts) >= 2 else filepath.name[:19]

            session = {
                "timestamp": timestamp,
                "model": data.get("model", {}).get("name", "Unknown"),
                "goal_coords_on": settings.get("showGoalCoordinates", "unknown"),
                "won": outcome.get("won", False),
                "stopped_by_user": outcome.get("stoppedByUser", False),
                "max_moves": outcome.get("reachedMaxMoves", False),
                "too_many_revisits": outcome.get("tooManyRevisits", False),
                "error": outcome.get("errorOccurred", False),
                "total_moves": metrics.get("totalMoves", 0),
                "successful_moves": metrics.get("successfulMoves", 0),
                "wall_collisions": metrics.get("wallCollisions", 0),
                "unique_positions": metrics.get("uniquePositionsVisited", 0),
                "backtracks": metrics.get("backtrackCount", 0),
                "total_tokens": tokens.get("totalTokens", 0),
                "cost_usd": cost.get("totalCostUsd", 0),
            }
            session["backtrack_rate"] = (
                session["backtracks"] / session["total_moves"] * 100
                if session["total_moves"] > 0 else 0
            )

            all_sessions.append(session)

            # Update model stats
            model = session["model"]
            model_stats[model]["runs"] += 1
            model_stats[model]["wins"] += 1 if session["won"] else 0
            model_stats[model]["moves"] += session["total_moves"]
            model_stats[model]["backtracks"] += session["backtracks"]
            model_stats[model]["collisions"] += session["wall_collisions"]

            # Update goal coords comparison
            if session["goal_coords_on"] == True:
                goal_on["runs"] += 1
                goal_on["wins"] += 1 if session["won"] else 0
            elif session["goal_coords_on"] == False:
                goal_off["runs"] += 1
                goal_off["wins"] += 1 if session["won"] else 0
            else:  # "unknown" - older sessions
                goal_unknown["runs"] += 1
                goal_unknown["wins"] += 1 if session["won"] else 0

        except Exception as e:
            print(f"Error parsing {filepath.name}: {e}")

    # Write CSV
    csv_path = Path("session_summary.csv")
    with open(csv_path, "w") as f:
        f.write("Timestamp,Model,GoalCoordsOn,Won,StoppedByUser,MaxMoves,TooManyRevisits,Error,TotalMoves,SuccessfulMoves,WallCollisions,UniquePositions,Backtracks,BacktrackRate,TotalTokens,CostUSD\n")
        for s in all_sessions:
            f.write(f"{s['timestamp']},{s['model']},{s['goal_coords_on']},{s['won']},{s['stopped_by_user']},{s['max_moves']},{s['too_many_revisits']},{s['error']},{s['total_moves']},{s['successful_moves']},{s['wall_collisions']},{s['unique_positions']},{s['backtracks']},{s['backtrack_rate']:.1f},{s['total_tokens']},{s['cost_usd']:.4f}\n")

    print(f"CSV written to: {csv_path}\n")

    # Print summary report
    total = len(all_sessions)
    wins = sum(1 for s in all_sessions if s["won"])

    print("=" * 65)
    print("SCIENCE FAIR DATA SUMMARY")
    print("=" * 65)
    print()
    print(f"Total Sessions: {total}")
    print(f"Total Wins: {wins} ({wins/total*100:.1f}%)" if total > 0 else "Total Wins: 0")
    print()

    print("--- KEY COMPARISON: Goal Coordinates ON vs OFF ---")
    print()
    on_pct = goal_on["wins"]/goal_on["runs"]*100 if goal_on["runs"] > 0 else 0
    off_pct = goal_off["wins"]/goal_off["runs"]*100 if goal_off["runs"] > 0 else 0
    unk_pct = goal_unknown["wins"]/goal_unknown["runs"]*100 if goal_unknown["runs"] > 0 else 0
    print(f"  Goal Coords ON:      {goal_on['runs']:3} runs, {goal_on['wins']:3} wins ({on_pct:.1f}% success)")
    print(f"  Goal Coords OFF:     {goal_off['runs']:3} runs, {goal_off['wins']:3} wins ({off_pct:.1f}% success)")
    if goal_unknown["runs"] > 0:
        print(f"  (Older sessions):    {goal_unknown['runs']:3} runs, {goal_unknown['wins']:3} wins ({unk_pct:.1f}% success) - setting unknown")
    print()

    if goal_off["runs"] == 0:
        print("  ** NO DATA with Goal Coordinates OFF! **")
        print("  To test the hypothesis, run sessions with ShowGoalCoordinates = false")
        print()

    print("--- BY MODEL ---")
    print()
    print(f"{'Model':<30} {'Runs':>6} {'Wins':>6} {'Win%':>7} {'AvgMoves':>9} {'Backtrk%':>9} {'Collisions':>11}")
    print("-" * 85)

    for model in sorted(model_stats.keys(), key=lambda m: -model_stats[m]["runs"]):
        stats = model_stats[model]
        avg_moves = stats["moves"] / stats["runs"] if stats["runs"] > 0 else 0
        backtrack_pct = stats["backtracks"] / stats["moves"] * 100 if stats["moves"] > 0 else 0
        win_pct = stats["wins"] / stats["runs"] * 100 if stats["runs"] > 0 else 0
        print(f"{model:<30} {stats['runs']:>6} {stats['wins']:>6} {win_pct:>6.1f}% {avg_moves:>9.1f} {backtrack_pct:>8.1f}% {stats['collisions']:>11}")

    print()
    print("=" * 65)
    print()
    print("Next steps:")
    print("1. Open session_summary.csv in Excel/Google Sheets")
    print("2. Create charts comparing Goal Coords ON vs OFF")
    print("3. Run more sessions with ShowGoalCoordinates = false")

if __name__ == "__main__":
    main()
