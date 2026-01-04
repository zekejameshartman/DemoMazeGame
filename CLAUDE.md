# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Run Commands

```bash
# Build the project
dotnet build

# Run the application (from project directory)
cd DempMazeGame && dotnet run
```

Note: The project folder is named "DempMazeGame" (typo in original).

## Project Overview

DemoMazeGame is a C# console application (.NET 10.0) that compares human and AI players solving a maze. It's designed as an experiment to test different LLM models' spatial reasoning abilities via the OpenRouter API.

## Architecture

**Program.cs** - Entry point, sets up DI container, manages main application loop, handles API key loading from .env file

**Game.cs** - Core game engine with 11x12 hardcoded maze. Two modes:
- `PlayAsHuman()` - Keyboard-driven N/S/E/W navigation
- `PlayAsAi()` - Async AI-driven play with visualization, session logging, and 200 move limit

**Menu.cs** - Console UI with main menu, model selection, and settings (coordinate display, ASCII map toggle, move delay)

**AiPlayer.cs** - OpenRouter API integration. Maintains conversation history, builds contextual prompts with visible cells, parses direction responses with token usage. Supports 5 models: Claude Haiku 4.5, GPT-4o Mini, Gemini 2.0 Flash, DeepSeek R1, Llama 4 Scout

### Service Layer (DI)

**Services/IAppLogger.cs & AppLogger.cs** - Application-wide logging to `logs/app.log`

**Services/IAiSessionLogger.cs & AiSessionLogger.cs** - Per-game JSON session logs to `logs/sessions/`. Each AI game creates a file named `{timestamp}_{model}.json` with:
- Model info and settings used
- Outcome (won, stopped by user, max moves, error)
- Metrics: total moves, wall collisions, unique positions, backtracks
- Token usage per move and totals
- Estimated cost (calculated from model pricing)
- Complete move history with positions

**Models/AiSessionLog.cs** - Data models for session logs

**Models/AiMoveResult.cs** - Result from AI moves including token usage

## Configuration

API key stored in `DempMazeGame/.env` as `OPENROUTER_API_KEY=...` (or via environment variable)

## Log Files

- `logs/app.log` - Application events and errors
- `logs/sessions/*.json` - AI game session data for analysis
