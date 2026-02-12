import chokidar from "chokidar";
import cors from "cors";
import express from "express";
import path from "node:path";
import { readSessionSummaries, resolveSessionsDir } from "./sessions.js";

const apiHost = process.env.ANALYTICS_API_HOST || "127.0.0.1";
const apiPort = Number(process.env.ANALYTICS_API_PORT || 5179);
const sessionsDir = resolveSessionsDir();

const app = express();
app.disable("x-powered-by");
app.use(cors());

let sessionCache = {
  sessions: [],
  count: 0,
  updatedAt: new Date().toISOString(),
};

let lastReloadError = "";
const sseClients = new Set();
let refreshTimer = null;
const pendingTriggers = new Set();

async function refreshCache(trigger) {
  try {
    const sessions = await readSessionSummaries(sessionsDir);
    sessionCache = {
      sessions,
      count: sessions.length,
      updatedAt: new Date().toISOString(),
    };
    lastReloadError = "";
  } catch (error) {
    lastReloadError = error instanceof Error ? error.message : String(error);
  }

  if (trigger) {
    broadcastEvent({
      type: "sessions_changed",
      trigger,
      count: sessionCache.count,
      updatedAt: sessionCache.updatedAt,
    });
  }
}

function scheduleRefresh(trigger) {
  pendingTriggers.add(trigger);

  if (refreshTimer) {
    return;
  }

  refreshTimer = setTimeout(async () => {
    refreshTimer = null;
    const triggerList = Array.from(pendingTriggers);
    pendingTriggers.clear();

    await refreshCache(triggerList.join(", "));
  }, 350);
}

function broadcastEvent(payload) {
  const data = `data: ${JSON.stringify(payload)}\n\n`;
  for (const res of sseClients) {
    try {
      res.write(data);
    } catch {
      sseClients.delete(res);
    }
  }
}

app.get("/api/health", (_req, res) => {
  res.json({
    ok: true,
    sessionsDir,
    sessionCount: sessionCache.count,
    updatedAt: sessionCache.updatedAt,
    lastReloadError,
  });
});

app.get("/api/sessions", async (req, res) => {
  if (req.query.refresh === "1") {
    await refreshCache("manual_refresh");
  }

  res.json({
    sessionsDir,
    updatedAt: sessionCache.updatedAt,
    count: sessionCache.count,
    sessions: sessionCache.sessions,
    lastReloadError,
  });
});

app.get("/api/events", (req, res) => {
  res.setHeader("Content-Type", "text/event-stream");
  res.setHeader("Cache-Control", "no-cache");
  res.setHeader("Connection", "keep-alive");
  res.flushHeaders();

  sseClients.add(res);

  res.write(
    `data: ${JSON.stringify({
      type: "connected",
      count: sessionCache.count,
      updatedAt: sessionCache.updatedAt,
    })}\n\n`,
  );

  const keepAlive = setInterval(() => {
    res.write(": keep-alive\n\n");
  }, 20000);

  req.on("close", () => {
    clearInterval(keepAlive);
    sseClients.delete(res);
  });
});

function startWatcher() {
  const watchPattern = path.join(sessionsDir, "*.json");

  const watcher = chokidar.watch(watchPattern, {
    ignoreInitial: true,
    awaitWriteFinish: {
      stabilityThreshold: 250,
      pollInterval: 100,
    },
  });

  watcher.on("all", (eventName, changedPath) => {
    if (changedPath.endsWith("_api.json")) {
      return;
    }
    scheduleRefresh(`fs:${eventName}`);
  });

  watcher.on("error", (error) => {
    lastReloadError = `watcher: ${error instanceof Error ? error.message : String(error)}`;
  });

  return watcher;
}

async function start() {
  await refreshCache("");

  const watcher = startWatcher();

  const server = app.listen(apiPort, apiHost, () => {
    process.stdout.write(
      `[analytics-api] listening on http://${apiHost}:${apiPort} (sessions: ${sessionsDir})\n`,
    );
  });

  const shutdown = async () => {
    if (refreshTimer) {
      clearTimeout(refreshTimer);
      refreshTimer = null;
    }

    await watcher.close();

    for (const res of sseClients) {
      try {
        res.end();
      } catch {
        // ignore
      }
    }
    sseClients.clear();

    server.close(() => {
      process.exit(0);
    });
  };

  process.on("SIGINT", shutdown);
  process.on("SIGTERM", shutdown);
}

start().catch((error) => {
  process.stderr.write(`[analytics-api] failed to start: ${error instanceof Error ? error.stack : String(error)}\n`);
  process.exit(1);
});
