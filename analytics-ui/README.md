# DemoMazeGame Analytics UI

Vue 3 + Chart.js live dashboard for AI maze sessions.

## What it does

- Reads session logs from `DempMazeGame/logs/sessions`
- Streams updates live when new session files are added or changed
- Provides chart exports (PNG), print-friendly layout, and CSV export
- Supports all-time default view, time-range filters, and last-X filters

## Requirements

- Node.js 18+

## Run

From `analytics-ui`:

```bash
npm install
npm run dev
```

- Web app: `http://127.0.0.1:5173`
- Local API: `http://127.0.0.1:5179`

## Optional environment variables

- `SESSIONS_DIR` - override session directory path
- `ANALYTICS_API_HOST` - API host (default `127.0.0.1`)
- `ANALYTICS_API_PORT` - API port (default `5179`)

Example:

```bash
SESSIONS_DIR="/path/to/your/sessions" npm run dev
```

## Build static frontend

```bash
npm run build
npm run preview
```

The API remains local and live in dev mode; the built frontend expects an API at `/api`.
