import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const analyticsRoot = path.resolve(__dirname, "..");
const repoRoot = path.resolve(analyticsRoot, "..");

const defaultSessionsDir = path.join(repoRoot, "DempMazeGame", "logs", "sessions");

export function resolveSessionsDir() {
  const overrideDir = process.env.SESSIONS_DIR?.trim();
  if (overrideDir) {
    return path.resolve(overrideDir);
  }
  return defaultSessionsDir;
}

export async function readSessionSummaries(sessionsDir) {
  let fileNames = [];

  try {
    fileNames = await fs.readdir(sessionsDir);
  } catch {
    return [];
  }

  const sessionFiles = fileNames
    .filter((name) => name.endsWith(".json") && !name.endsWith("_api.json"))
    .sort((a, b) => b.localeCompare(a));

  const summaries = await Promise.all(
    sessionFiles.map(async (fileName) => {
      const filePath = path.join(sessionsDir, fileName);
      try {
        const raw = await fs.readFile(filePath, "utf8");
        const parsed = JSON.parse(raw);
        return toSessionSummary(parsed, fileName);
      } catch {
        return null;
      }
    }),
  );

  return summaries
    .filter(Boolean)
    .sort((a, b) => (b.startTime || "").localeCompare(a.startTime || ""));
}

function toSessionSummary(input, fileName) {
  const modelName = toString(input?.model?.name) || "Unknown";
  const modelId = toString(input?.model?.id);

  const startTime = toDateIso(input?.startTime) ?? parseFileTimestampToIso(fileName) ?? "";
  const endTime = toDateIso(input?.endTime);

  const durationSeconds =
    toNumber(input?.durationSeconds) ||
    computeDurationSeconds(startTime, endTime) ||
    0;

  const won = toBool(input?.outcome?.won);
  const stoppedByUser = toBool(input?.outcome?.stoppedByUser);
  const reachedMaxMoves = toBool(input?.outcome?.reachedMaxMoves);
  const tooManyRevisits = toBool(input?.outcome?.tooManyRevisits);
  const errorOccurred = toBool(input?.outcome?.errorOccurred);

  return {
    sessionId: toString(input?.sessionId) || fileName.replace(/\.json$/i, ""),
    fileName,
    startTime,
    endTime,
    durationSeconds,
    modelName,
    modelId,

    showGoalCoordinates: toNullableBool(input?.settings?.showGoalCoordinates),
    breadcrumbs: toNullableBool(input?.settings?.breadcrumbs),
    distanceToWall: toNullableBool(input?.settings?.distanceToWall),
    reasoningEnabled: toNullableBool(input?.settings?.reasoningEnabled),

    won,
    stoppedByUser,
    reachedMaxMoves,
    tooManyRevisits,
    errorOccurred,
    outcomeType: computeOutcomeType({
      won,
      stoppedByUser,
      reachedMaxMoves,
      tooManyRevisits,
      errorOccurred,
    }),

    totalMoves: toNumber(input?.metrics?.totalMoves),
    wallCollisions: toNumber(input?.metrics?.wallCollisions),
    backtracks: toNumber(input?.metrics?.backtrackCount),
    uniquePositionsVisited: toNumber(input?.metrics?.uniquePositionsVisited),

    totalTokens: toNumber(input?.tokenUsage?.totalTokens),
    totalCostUsd: toNumber(input?.cost?.totalCostUsd),
  };
}

function computeOutcomeType({ won, stoppedByUser, reachedMaxMoves, tooManyRevisits, errorOccurred }) {
  if (won) return "won";
  if (stoppedByUser) return "stopped";
  if (reachedMaxMoves) return "max_moves";
  if (tooManyRevisits) return "loops";
  if (errorOccurred) return "error";
  return "unknown";
}

function parseFileTimestampToIso(fileName) {
  const match = /^(\d{4})-(\d{2})-(\d{2})_(\d{2})(\d{2})(\d{2})(?:_(\d{3}))?/.exec(fileName);
  if (!match) return null;

  const [, yyyy, mm, dd, hh, min, sec, millis = "0"] = match;
  const iso = new Date(
    Date.UTC(
      Number(yyyy),
      Number(mm) - 1,
      Number(dd),
      Number(hh),
      Number(min),
      Number(sec),
      Number(millis),
    ),
  ).toISOString();

  return iso;
}

function computeDurationSeconds(startIso, endIso) {
  if (!startIso || !endIso) return null;
  const start = new Date(startIso).getTime();
  const end = new Date(endIso).getTime();
  if (Number.isNaN(start) || Number.isNaN(end) || end < start) return null;
  return (end - start) / 1000;
}

function toDateIso(value) {
  if (typeof value !== "string" || !value.trim()) return null;
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return null;
  return parsed.toISOString();
}

function toBool(value) {
  return value === true;
}

function toNullableBool(value) {
  if (value === true) return true;
  if (value === false) return false;
  return null;
}

function toNumber(value) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) return 0;
  return parsed;
}

function toString(value) {
  if (typeof value === "string") return value.trim();
  return "";
}
