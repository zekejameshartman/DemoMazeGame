<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from "vue";
import { Bar, Line } from "vue-chartjs";

const OUTCOME_META = [
  { key: "won", label: "Won", color: "#0f766e" },
  { key: "max_moves", label: "Max Moves", color: "#d97706" },
  { key: "loops", label: "Loops", color: "#ea580c" },
  { key: "error", label: "Error", color: "#b91c1c" },
  { key: "stopped", label: "Stopped", color: "#475569" },
  { key: "unknown", label: "Unknown", color: "#6b7280" },
];

const outcomeLabelMap = Object.fromEntries(OUTCOME_META.map((entry) => [entry.key, entry.label]));

const sessions = ref([]);
const isLoading = ref(false);
const loadError = ref("");
const apiWarning = ref("");
const lastSyncAt = ref("");
const liveStatus = ref("connecting");

const winRateChartRef = ref(null);
const outcomeChartRef = ref(null);
const performanceChartRef = ref(null);
const trendChartRef = ref(null);
const goalImpactChartRef = ref(null);

const filters = reactive({
  models: [],
  outcomes: OUTCOME_META.map((entry) => entry.key),
  goalCoordinates: "all",
  reasoning: "all",
  timeRange: "all",
  customStart: "",
  customEnd: "",
  lastX: "all",
  lastXCustom: 25,
});

const dateTimeFormatter = new Intl.DateTimeFormat("en-US", {
  year: "numeric",
  month: "short",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
});

const dayFormatter = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "2-digit",
});

let eventSource = null;
let reconnectTimeoutId = null;
let refreshDebounceTimeoutId = null;
let fallbackPollIntervalId = null;

const modelOptions = computed(() => {
  const unique = new Set();
  for (const session of sessions.value) {
    unique.add(session.modelName || "Unknown");
  }
  return Array.from(unique).sort((a, b) => a.localeCompare(b));
});

watch(modelOptions, (options) => {
  const valid = new Set(options);
  filters.models = filters.models.filter((model) => valid.has(model));
});

const effectiveLastX = computed(() => {
  if (filters.lastX === "all") {
    return null;
  }

  if (filters.lastX === "custom") {
    const parsed = Number(filters.lastXCustom);
    if (Number.isFinite(parsed) && parsed > 0) {
      return Math.floor(parsed);
    }
    return null;
  }

  const parsed = Number(filters.lastX);
  if (Number.isFinite(parsed) && parsed > 0) {
    return Math.floor(parsed);
  }
  return null;
});

const activeTimeBounds = computed(() => {
  const now = Date.now();

  switch (filters.timeRange) {
    case "24h":
      return { start: now - 24 * 60 * 60 * 1000, end: null };
    case "7d":
      return { start: now - 7 * 24 * 60 * 60 * 1000, end: null };
    case "30d":
      return { start: now - 30 * 24 * 60 * 60 * 1000, end: null };
    case "90d":
      return { start: now - 90 * 24 * 60 * 60 * 1000, end: null };
    case "custom":
      return {
        start: parseLocalDateTime(filters.customStart),
        end: parseLocalDateTime(filters.customEnd),
      };
    default:
      return { start: null, end: null };
  }
});

const filteredSessions = computed(() => {
  const selectedModels = new Set(filters.models);
  const selectedOutcomes = new Set(filters.outcomes);
  const bounds = activeTimeBounds.value;

  let rows = sessions.value.filter((session) => {
    if (selectedModels.size > 0 && !selectedModels.has(session.modelName)) {
      return false;
    }

    if (selectedOutcomes.size === 0) {
      return false;
    }

    if (!selectedOutcomes.has(session.outcomeType)) {
      return false;
    }

    if (!matchesGoalFilter(session.showGoalCoordinates, filters.goalCoordinates)) {
      return false;
    }

    if (!matchesReasoningFilter(session.reasoningEnabled, filters.reasoning)) {
      return false;
    }

    if (bounds.start != null && session.startEpoch < bounds.start) {
      return false;
    }

    if (bounds.end != null && session.startEpoch > bounds.end) {
      return false;
    }

    return true;
  });

  rows = rows.sort((a, b) => b.startEpoch - a.startEpoch);

  if (effectiveLastX.value != null) {
    rows = rows.slice(0, effectiveLastX.value);
  }

  return rows;
});

const hasFilteredData = computed(() => filteredSessions.value.length > 0);

const kpis = computed(() => {
  const source = filteredSessions.value;
  const runs = source.length;
  const wins = source.filter((session) => session.won).length;

  const totals = source.reduce(
    (acc, session) => {
      acc.moves += session.totalMoves;
      acc.collisions += session.wallCollisions;
      acc.backtracks += session.backtracks;
      acc.tokens += session.totalTokens;
      acc.cost += session.totalCostUsd;
      acc.duration += session.durationSeconds;
      return acc;
    },
    {
      moves: 0,
      collisions: 0,
      backtracks: 0,
      tokens: 0,
      cost: 0,
      duration: 0,
    },
  );

  return {
    runs,
    wins,
    winRate: runs > 0 ? (wins / runs) * 100 : 0,
    avgMoves: runs > 0 ? totals.moves / runs : 0,
    avgCollisions: runs > 0 ? totals.collisions / runs : 0,
    avgBacktracks: runs > 0 ? totals.backtracks / runs : 0,
    avgTokens: runs > 0 ? totals.tokens / runs : 0,
    totalCost: totals.cost,
    avgCost: runs > 0 ? totals.cost / runs : 0,
    avgDuration: runs > 0 ? totals.duration / runs : 0,
  };
});

const modelSummary = computed(() => {
  const map = new Map();

  for (const session of filteredSessions.value) {
    const modelName = session.modelName || "Unknown";

    if (!map.has(modelName)) {
      map.set(modelName, {
        model: modelName,
        runs: 0,
        wins: 0,
        totalMoves: 0,
        totalCollisions: 0,
        totalBacktracks: 0,
        totalTokens: 0,
        totalCost: 0,
        outcomes: Object.fromEntries(OUTCOME_META.map((entry) => [entry.key, 0])),
      });
    }

    const row = map.get(modelName);
    row.runs += 1;
    row.wins += session.won ? 1 : 0;
    row.totalMoves += session.totalMoves;
    row.totalCollisions += session.wallCollisions;
    row.totalBacktracks += session.backtracks;
    row.totalTokens += session.totalTokens;
    row.totalCost += session.totalCostUsd;
    row.outcomes[session.outcomeType] += 1;
  }

  return Array.from(map.values())
    .map((row) => ({
      ...row,
      winRate: row.runs > 0 ? (row.wins / row.runs) * 100 : 0,
      avgMoves: row.runs > 0 ? row.totalMoves / row.runs : 0,
      avgCollisions: row.runs > 0 ? row.totalCollisions / row.runs : 0,
      avgBacktracks: row.runs > 0 ? row.totalBacktracks / row.runs : 0,
      avgTokens: row.runs > 0 ? row.totalTokens / row.runs : 0,
      avgCost: row.runs > 0 ? row.totalCost / row.runs : 0,
    }))
    .sort((a, b) => b.runs - a.runs || a.model.localeCompare(b.model));
});

const winRateChartData = computed(() => {
  const labels = modelSummary.value.map((row) => row.model);
  const data = modelSummary.value.map((row) => roundNumber(row.winRate, 1));

  return {
    labels,
    datasets: [
      {
        label: "Win Rate (%)",
        data,
        backgroundColor: "rgba(15, 118, 110, 0.82)",
        borderColor: "rgba(13, 148, 136, 1)",
        borderWidth: 1,
        borderRadius: 7,
      },
    ],
  };
});

const outcomeBreakdownChartData = computed(() => {
  const labels = modelSummary.value.map((row) => row.model);
  const datasets = OUTCOME_META.map((entry) => ({
    label: entry.label,
    data: modelSummary.value.map((row) => row.outcomes[entry.key] || 0),
    backgroundColor: `${entry.color}cc`,
    borderColor: entry.color,
    borderWidth: 1,
    stack: "outcomes",
  }));

  return { labels, datasets };
});

const performanceChartData = computed(() => {
  const labels = modelSummary.value.map((row) => row.model);

  return {
    labels,
    datasets: [
      {
        label: "Avg Moves",
        data: modelSummary.value.map((row) => roundNumber(row.avgMoves, 2)),
        backgroundColor: "rgba(30, 64, 175, 0.82)",
        borderColor: "rgba(37, 99, 235, 1)",
        borderWidth: 1,
        borderRadius: 7,
      },
      {
        label: "Avg Collisions",
        data: modelSummary.value.map((row) => roundNumber(row.avgCollisions, 2)),
        backgroundColor: "rgba(217, 119, 6, 0.82)",
        borderColor: "rgba(245, 158, 11, 1)",
        borderWidth: 1,
        borderRadius: 7,
      },
      {
        label: "Avg Backtracks",
        data: modelSummary.value.map((row) => roundNumber(row.avgBacktracks, 2)),
        backgroundColor: "rgba(153, 27, 27, 0.82)",
        borderColor: "rgba(220, 38, 38, 1)",
        borderWidth: 1,
        borderRadius: 7,
      },
    ],
  };
});

const trendByDay = computed(() => {
  const rows = [...filteredSessions.value].sort((a, b) => a.startEpoch - b.startEpoch);
  const map = new Map();

  for (const session of rows) {
    const dayKey = toIsoDay(session.startTime);
    if (!map.has(dayKey)) {
      map.set(dayKey, {
        dayKey,
        totalCost: 0,
        totalTokens: 0,
        runs: 0,
      });
    }

    const bucket = map.get(dayKey);
    bucket.totalCost += session.totalCostUsd;
    bucket.totalTokens += session.totalTokens;
    bucket.runs += 1;
  }

  return Array.from(map.values()).sort((a, b) => a.dayKey.localeCompare(b.dayKey));
});

const trendChartData = computed(() => {
  return {
    labels: trendByDay.value.map((entry) => formatIsoDay(entry.dayKey)),
    datasets: [
      {
        type: "line",
        label: "Total Cost (USD)",
        data: trendByDay.value.map((entry) => roundNumber(entry.totalCost, 4)),
        yAxisID: "costAxis",
        tension: 0.25,
        borderColor: "rgba(14, 116, 144, 1)",
        backgroundColor: "rgba(14, 116, 144, 0.22)",
        pointBackgroundColor: "rgba(14, 116, 144, 1)",
        fill: true,
      },
      {
        type: "line",
        label: "Total Tokens (thousands)",
        data: trendByDay.value.map((entry) => roundNumber(entry.totalTokens / 1000, 1)),
        yAxisID: "tokensAxis",
        tension: 0.25,
        borderColor: "rgba(5, 150, 105, 1)",
        backgroundColor: "rgba(5, 150, 105, 0.16)",
        pointBackgroundColor: "rgba(5, 150, 105, 1)",
        fill: true,
      },
    ],
  };
});

const goalImpactSummary = computed(() => {
  const buckets = {
    on: { label: "Goal ON", runs: 0, wins: 0 },
    off: { label: "Goal OFF", runs: 0, wins: 0 },
    unknown: { label: "Goal Unknown", runs: 0, wins: 0 },
  };

  for (const session of filteredSessions.value) {
    const key =
      session.showGoalCoordinates === true
        ? "on"
        : session.showGoalCoordinates === false
          ? "off"
          : "unknown";

    buckets[key].runs += 1;
    buckets[key].wins += session.won ? 1 : 0;
  }

  return [buckets.on, buckets.off, buckets.unknown].map((bucket) => ({
    ...bucket,
    winRate: bucket.runs > 0 ? (bucket.wins / bucket.runs) * 100 : 0,
  }));
});

const goalImpactChartData = computed(() => {
  return {
    labels: goalImpactSummary.value.map((entry) => entry.label),
    datasets: [
      {
        type: "bar",
        label: "Runs",
        data: goalImpactSummary.value.map((entry) => entry.runs),
        yAxisID: "runsAxis",
        backgroundColor: "rgba(30, 64, 175, 0.82)",
        borderColor: "rgba(37, 99, 235, 1)",
        borderWidth: 1,
        borderRadius: 7,
      },
      {
        type: "line",
        label: "Win Rate (%)",
        data: goalImpactSummary.value.map((entry) => roundNumber(entry.winRate, 1)),
        yAxisID: "rateAxis",
        tension: 0.2,
        borderColor: "rgba(217, 119, 6, 1)",
        backgroundColor: "rgba(217, 119, 6, 0.16)",
        pointBackgroundColor: "rgba(217, 119, 6, 1)",
        fill: false,
      },
    ],
  };
});

const chartBaseOptions = {
  responsive: true,
  maintainAspectRatio: false,
  interaction: {
    mode: "index",
    intersect: false,
  },
  plugins: {
    legend: {
      labels: {
        color: "#1f2937",
        font: {
          family: "Sora",
          size: 12,
        },
      },
    },
  },
  scales: {
    x: {
      ticks: {
        color: "#334155",
      },
      grid: {
        color: "rgba(15, 23, 42, 0.06)",
      },
    },
    y: {
      beginAtZero: true,
      ticks: {
        color: "#334155",
      },
      grid: {
        color: "rgba(15, 23, 42, 0.08)",
      },
    },
  },
};

const winRateChartOptions = {
  ...chartBaseOptions,
  scales: {
    ...chartBaseOptions.scales,
    y: {
      ...chartBaseOptions.scales.y,
      max: 100,
      ticks: {
        ...chartBaseOptions.scales.y.ticks,
        callback: (value) => `${value}%`,
      },
    },
  },
};

const stackedOutcomeChartOptions = {
  ...chartBaseOptions,
  scales: {
    x: {
      ...chartBaseOptions.scales.x,
      stacked: true,
    },
    y: {
      ...chartBaseOptions.scales.y,
      stacked: true,
    },
  },
};

const trendChartOptions = {
  ...chartBaseOptions,
  scales: {
    x: {
      ...chartBaseOptions.scales.x,
    },
    costAxis: {
      beginAtZero: true,
      position: "left",
      ticks: {
        color: "#155e75",
        callback: (value) => `$${Number(value).toFixed(3)}`,
      },
      grid: {
        color: "rgba(14, 116, 144, 0.1)",
      },
      title: {
        display: true,
        text: "Cost (USD)",
        color: "#155e75",
      },
    },
    tokensAxis: {
      beginAtZero: true,
      position: "right",
      ticks: {
        color: "#065f46",
        callback: (value) => `${Number(value).toFixed(0)}k`,
      },
      grid: {
        drawOnChartArea: false,
      },
      title: {
        display: true,
        text: "Tokens (thousands)",
        color: "#065f46",
      },
    },
  },
};

const goalImpactChartOptions = {
  ...chartBaseOptions,
  scales: {
    x: {
      ...chartBaseOptions.scales.x,
    },
    runsAxis: {
      beginAtZero: true,
      position: "left",
      ticks: {
        color: "#1e3a8a",
      },
      title: {
        display: true,
        text: "Runs",
        color: "#1e3a8a",
      },
      grid: {
        color: "rgba(30, 64, 175, 0.1)",
      },
    },
    rateAxis: {
      beginAtZero: true,
      max: 100,
      position: "right",
      ticks: {
        color: "#92400e",
        callback: (value) => `${value}%`,
      },
      title: {
        display: true,
        text: "Win Rate",
        color: "#92400e",
      },
      grid: {
        drawOnChartArea: false,
      },
    },
  },
};

const liveStatusLabel = computed(() => {
  if (liveStatus.value === "connected") return "Live connected";
  if (liveStatus.value === "reconnecting") return "Reconnecting";
  return "Connecting";
});

const filteredCountLabel = computed(() => `${filteredSessions.value.length} shown / ${sessions.value.length} total`);

const timeRangeDescription = computed(() => {
  switch (filters.timeRange) {
    case "24h":
      return "Last 24 hours";
    case "7d":
      return "Last 7 days";
    case "30d":
      return "Last 30 days";
    case "90d":
      return "Last 90 days";
    case "custom":
      return "Custom range";
    default:
      return "All-time";
  }
});

async function refreshSessions({ silent = false } = {}) {
  if (!silent) {
    isLoading.value = true;
  }

  try {
    const response = await fetch(`/api/sessions?t=${Date.now()}`, {
      cache: "no-store",
    });

    if (!response.ok) {
      throw new Error(`Failed to load sessions (${response.status})`);
    }

    const payload = await response.json();
    const source = Array.isArray(payload.sessions) ? payload.sessions : [];

    sessions.value = source
      .map((row) => normalizeSession(row))
      .sort((a, b) => b.startEpoch - a.startEpoch);

    lastSyncAt.value = payload.updatedAt || new Date().toISOString();
    apiWarning.value = payload.lastReloadError || "";
    loadError.value = "";
  } catch (error) {
    loadError.value = error instanceof Error ? error.message : String(error);
  } finally {
    if (!silent) {
      isLoading.value = false;
    }
  }
}

function connectLiveEvents() {
  disconnectLiveEvents();
  liveStatus.value = "connecting";

  eventSource = new EventSource("/api/events");

  eventSource.onopen = () => {
    liveStatus.value = "connected";
  };

  eventSource.onmessage = (event) => {
    let payload;

    try {
      payload = JSON.parse(event.data);
    } catch {
      return;
    }

    if (payload.type === "connected") {
      liveStatus.value = "connected";
      if (payload.updatedAt) {
        lastSyncAt.value = payload.updatedAt;
      }
      return;
    }

    if (payload.type === "sessions_changed") {
      scheduleRefresh();
    }
  };

  eventSource.onerror = () => {
    liveStatus.value = "reconnecting";
    disconnectLiveEvents();
    scheduleReconnect();
  };
}

function disconnectLiveEvents() {
  if (eventSource) {
    eventSource.close();
    eventSource = null;
  }
}

function scheduleReconnect() {
  if (reconnectTimeoutId != null) {
    return;
  }

  reconnectTimeoutId = window.setTimeout(() => {
    reconnectTimeoutId = null;
    connectLiveEvents();
  }, 2500);
}

function scheduleRefresh() {
  if (refreshDebounceTimeoutId != null) {
    return;
  }

  refreshDebounceTimeoutId = window.setTimeout(async () => {
    refreshDebounceTimeoutId = null;
    await refreshSessions({ silent: true });
  }, 400);
}

function clearTimers() {
  if (reconnectTimeoutId != null) {
    clearTimeout(reconnectTimeoutId);
    reconnectTimeoutId = null;
  }

  if (refreshDebounceTimeoutId != null) {
    clearTimeout(refreshDebounceTimeoutId);
    refreshDebounceTimeoutId = null;
  }

  if (fallbackPollIntervalId != null) {
    clearInterval(fallbackPollIntervalId);
    fallbackPollIntervalId = null;
  }
}

function selectAllModels() {
  filters.models = [...modelOptions.value];
}

function clearModels() {
  filters.models = [];
}

function selectAllOutcomes() {
  filters.outcomes = OUTCOME_META.map((entry) => entry.key);
}

function clearOutcomes() {
  filters.outcomes = [];
}

function resetFilters() {
  filters.models = [];
  filters.outcomes = OUTCOME_META.map((entry) => entry.key);
  filters.goalCoordinates = "all";
  filters.reasoning = "all";
  filters.timeRange = "all";
  filters.customStart = "";
  filters.customEnd = "";
  filters.lastX = "all";
  filters.lastXCustom = 25;
}

function formatDateTime(value) {
  if (!value) return "-";
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return "-";
  return dateTimeFormatter.format(parsed);
}

function formatInteger(value) {
  return Math.round(value || 0).toLocaleString("en-US");
}

function formatMoney(value, decimals = 4) {
  return `$${(value || 0).toFixed(decimals)}`;
}

function formatPercent(value) {
  return `${(value || 0).toFixed(1)}%`;
}

function formatDuration(seconds) {
  if (!seconds || seconds < 1) {
    return "0s";
  }

  if (seconds < 60) {
    return `${seconds.toFixed(1)}s`;
  }

  const minutes = Math.floor(seconds / 60);
  const remainder = Math.round(seconds % 60);
  return `${minutes}m ${remainder}s`;
}

function formatOutcomeLabel(outcomeType) {
  return outcomeLabelMap[outcomeType] || "Unknown";
}

function formatGoalLabel(value) {
  if (value === true) return "On";
  if (value === false) return "Off";
  return "Unknown";
}

function formatReasoningLabel(value) {
  if (value === true) return "Enabled";
  if (value === false) return "Disabled";
  return "Unknown";
}

function formatLastSync(value) {
  if (!value) return "-";
  return formatDateTime(value);
}

function printBoard() {
  window.print();
}

function downloadChart(chartRef, fileBaseName) {
  const chart = chartRef?.value?.chart;
  if (!chart) {
    return;
  }

  const url = chart.toBase64Image("image/png", 1);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = `${fileBaseName}-${makeTimestampLabel()}.png`;
  anchor.click();
}

function exportCsv() {
  const headers = [
    "Start Time",
    "Model",
    "Outcome",
    "Moves",
    "Wall Collisions",
    "Backtracks",
    "Total Tokens",
    "Total Cost USD",
    "Goal Coordinates",
    "Reasoning",
  ];

  const rows = filteredSessions.value.map((session) => [
    session.startTime,
    session.modelName,
    formatOutcomeLabel(session.outcomeType),
    session.totalMoves,
    session.wallCollisions,
    session.backtracks,
    session.totalTokens,
    session.totalCostUsd,
    formatGoalLabel(session.showGoalCoordinates),
    formatReasoningLabel(session.reasoningEnabled),
  ]);

  const csv = [headers, ...rows]
    .map((row) => row.map((value) => csvEscape(value)).join(","))
    .join("\n");

  const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = `demomazegame-sessions-${makeTimestampLabel()}.csv`;
  anchor.click();
  URL.revokeObjectURL(url);
}

function normalizeSession(row) {
  const startIso = normalizeIsoDate(row.startTime);
  const endIso = normalizeIsoDate(row.endTime);

  return {
    sessionId: typeof row.sessionId === "string" ? row.sessionId : "",
    fileName: typeof row.fileName === "string" ? row.fileName : "",
    modelName: typeof row.modelName === "string" && row.modelName.trim() ? row.modelName : "Unknown",
    modelId: typeof row.modelId === "string" ? row.modelId : "",
    startTime: startIso,
    endTime: endIso,
    startEpoch: startIso ? new Date(startIso).getTime() : 0,
    durationSeconds: safeNumber(row.durationSeconds),
    showGoalCoordinates: normalizeNullableBool(row.showGoalCoordinates),
    reasoningEnabled: normalizeNullableBool(row.reasoningEnabled),
    won: row.won === true,
    stoppedByUser: row.stoppedByUser === true,
    reachedMaxMoves: row.reachedMaxMoves === true,
    tooManyRevisits: row.tooManyRevisits === true,
    errorOccurred: row.errorOccurred === true,
    outcomeType: typeof row.outcomeType === "string" ? row.outcomeType : "unknown",
    totalMoves: safeNumber(row.totalMoves),
    wallCollisions: safeNumber(row.wallCollisions),
    backtracks: safeNumber(row.backtracks),
    uniquePositionsVisited: safeNumber(row.uniquePositionsVisited),
    totalTokens: safeNumber(row.totalTokens),
    totalCostUsd: safeNumber(row.totalCostUsd),
  };
}

function normalizeIsoDate(value) {
  if (typeof value !== "string" || !value) {
    return "";
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return "";
  }

  return parsed.toISOString();
}

function normalizeNullableBool(value) {
  if (value === true) return true;
  if (value === false) return false;
  return null;
}

function safeNumber(value) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) return 0;
  return parsed;
}

function matchesGoalFilter(value, filterValue) {
  if (filterValue === "all") return true;
  if (filterValue === "on") return value === true;
  if (filterValue === "off") return value === false;
  return value == null;
}

function matchesReasoningFilter(value, filterValue) {
  if (filterValue === "all") return true;
  if (filterValue === "enabled") return value === true;
  if (filterValue === "disabled") return value === false;
  return value == null;
}

function parseLocalDateTime(value) {
  if (!value) return null;
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return null;
  return parsed.getTime();
}

function toIsoDay(isoString) {
  if (!isoString) {
    return "unknown";
  }

  if (isoString.length >= 10) {
    return isoString.slice(0, 10);
  }

  return "unknown";
}

function formatIsoDay(value) {
  if (!value || value === "unknown") {
    return "Unknown";
  }

  const parsed = new Date(`${value}T00:00:00Z`);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  return dayFormatter.format(parsed);
}

function roundNumber(value, decimals) {
  const factor = 10 ** decimals;
  return Math.round((value || 0) * factor) / factor;
}

function csvEscape(value) {
  const text = value == null ? "" : String(value);
  if (text.includes(",") || text.includes("\n") || text.includes('"')) {
    return `"${text.replaceAll('"', '""')}"`;
  }
  return text;
}

function makeTimestampLabel() {
  return new Date().toISOString().replace(/[.:]/g, "-");
}

onMounted(async () => {
  await refreshSessions();
  connectLiveEvents();

  fallbackPollIntervalId = window.setInterval(() => {
    refreshSessions({ silent: true });
  }, 60000);
});

onBeforeUnmount(() => {
  disconnectLiveEvents();
  clearTimers();
});
</script>

<template>
  <div class="page-shell">
    <header class="topbar">
      <div class="topbar-brand">
        <p class="eyebrow">DemoMazeGame Analytics</p>
        <h1>Live Session Dashboard</h1>
        <p class="subtext">
          {{ filteredCountLabel }}
          <span class="dot">•</span>
          {{ timeRangeDescription }}
        </p>
      </div>

      <div class="topbar-meta">
        <div class="live-chip" :class="`live-chip-${liveStatus}`">
          {{ liveStatusLabel }}
        </div>
        <p class="meta-line">Last sync: {{ formatLastSync(lastSyncAt) }}</p>
      </div>

      <div class="topbar-actions">
        <button class="btn btn-quiet" @click="refreshSessions()" :disabled="isLoading">
          {{ isLoading ? "Refreshing..." : "Refresh" }}
        </button>
        <button class="btn btn-quiet" @click="exportCsv">Export CSV</button>
        <button class="btn btn-solid" @click="printBoard">Print Layout</button>
      </div>
    </header>

    <main class="workspace">
      <aside class="panel filters-panel">
        <div class="panel-header">
          <h2>Filters</h2>
          <button class="text-btn" @click="resetFilters">Reset</button>
        </div>

        <div class="field-group">
          <div class="field-head">
            <label for="modelSelect">Models</label>
            <div class="inline-actions">
              <button class="text-btn" @click="selectAllModels">All</button>
              <button class="text-btn" @click="clearModels">None</button>
            </div>
          </div>
          <select id="modelSelect" multiple size="7" v-model="filters.models">
            <option v-for="modelName in modelOptions" :key="modelName" :value="modelName">
              {{ modelName }}
            </option>
          </select>
          <p class="hint">Leave empty to include all models.</p>
        </div>

        <div class="field-group">
          <label for="goalSelect">Goal Coordinates</label>
          <select id="goalSelect" v-model="filters.goalCoordinates">
            <option value="all">All</option>
            <option value="on">On</option>
            <option value="off">Off</option>
            <option value="unknown">Unknown</option>
          </select>
        </div>

        <div class="field-group">
          <label for="reasoningSelect">Reasoning</label>
          <select id="reasoningSelect" v-model="filters.reasoning">
            <option value="all">All</option>
            <option value="enabled">Enabled</option>
            <option value="disabled">Disabled</option>
            <option value="unknown">Unknown</option>
          </select>
        </div>

        <div class="field-group">
          <label for="timeRangeSelect">Time Range</label>
          <select id="timeRangeSelect" v-model="filters.timeRange">
            <option value="all">All-time</option>
            <option value="24h">Last 24 hours</option>
            <option value="7d">Last 7 days</option>
            <option value="30d">Last 30 days</option>
            <option value="90d">Last 90 days</option>
            <option value="custom">Custom</option>
          </select>

          <div v-if="filters.timeRange === 'custom'" class="split-field">
            <label>
              Start
              <input type="datetime-local" v-model="filters.customStart" />
            </label>
            <label>
              End
              <input type="datetime-local" v-model="filters.customEnd" />
            </label>
          </div>
        </div>

        <div class="field-group">
          <label for="lastXSelect">Last X Sessions</label>
          <select id="lastXSelect" v-model="filters.lastX">
            <option value="all">All</option>
            <option value="10">Last 10</option>
            <option value="25">Last 25</option>
            <option value="50">Last 50</option>
            <option value="100">Last 100</option>
            <option value="custom">Custom</option>
          </select>

          <div v-if="filters.lastX === 'custom'" class="single-input">
            <label>
              Count
              <input type="number" min="1" step="1" v-model.number="filters.lastXCustom" />
            </label>
          </div>
        </div>

        <div class="field-group">
          <div class="field-head">
            <label>Outcomes</label>
            <div class="inline-actions">
              <button class="text-btn" @click="selectAllOutcomes">All</button>
              <button class="text-btn" @click="clearOutcomes">None</button>
            </div>
          </div>

          <div class="outcome-grid">
            <label v-for="entry in OUTCOME_META" :key="entry.key" class="checkbox-pill">
              <input type="checkbox" :value="entry.key" v-model="filters.outcomes" />
              <span>{{ entry.label }}</span>
            </label>
          </div>
        </div>
      </aside>

      <section class="content-area">
        <section class="kpi-grid">
          <article class="panel kpi-card">
            <p class="kpi-label">Runs</p>
            <p class="kpi-value">{{ formatInteger(kpis.runs) }}</p>
          </article>
          <article class="panel kpi-card">
            <p class="kpi-label">Wins</p>
            <p class="kpi-value">{{ formatInteger(kpis.wins) }}</p>
          </article>
          <article class="panel kpi-card">
            <p class="kpi-label">Win Rate</p>
            <p class="kpi-value">{{ formatPercent(kpis.winRate) }}</p>
          </article>
          <article class="panel kpi-card">
            <p class="kpi-label">Avg Moves</p>
            <p class="kpi-value">{{ roundNumber(kpis.avgMoves, 1) }}</p>
          </article>
          <article class="panel kpi-card">
            <p class="kpi-label">Avg Collisions</p>
            <p class="kpi-value">{{ roundNumber(kpis.avgCollisions, 1) }}</p>
          </article>
          <article class="panel kpi-card">
            <p class="kpi-label">Avg Backtracks</p>
            <p class="kpi-value">{{ roundNumber(kpis.avgBacktracks, 1) }}</p>
          </article>
          <article class="panel kpi-card">
            <p class="kpi-label">Avg Tokens</p>
            <p class="kpi-value">{{ formatInteger(kpis.avgTokens) }}</p>
          </article>
          <article class="panel kpi-card">
            <p class="kpi-label">Avg Cost</p>
            <p class="kpi-value">{{ formatMoney(kpis.avgCost, 4) }}</p>
          </article>
          <article class="panel kpi-card">
            <p class="kpi-label">Total Cost</p>
            <p class="kpi-value">{{ formatMoney(kpis.totalCost, 4) }}</p>
          </article>
          <article class="panel kpi-card">
            <p class="kpi-label">Avg Duration</p>
            <p class="kpi-value">{{ formatDuration(kpis.avgDuration) }}</p>
          </article>
        </section>

        <p v-if="loadError" class="banner banner-error">{{ loadError }}</p>
        <p v-else-if="apiWarning" class="banner banner-warning">{{ apiWarning }}</p>

        <section class="chart-grid">
          <article class="panel chart-card">
            <div class="card-header">
              <h3>Win Rate by Model</h3>
              <button class="text-btn" @click="downloadChart(winRateChartRef, 'win-rate-by-model')">PNG</button>
            </div>
            <div v-if="hasFilteredData" class="chart-wrap">
              <Bar ref="winRateChartRef" :data="winRateChartData" :options="winRateChartOptions" />
            </div>
            <p v-else class="empty-state">No sessions match the current filters.</p>
          </article>

          <article class="panel chart-card">
            <div class="card-header">
              <h3>Outcome Breakdown by Model</h3>
              <button class="text-btn" @click="downloadChart(outcomeChartRef, 'outcome-breakdown')">PNG</button>
            </div>
            <div v-if="hasFilteredData" class="chart-wrap">
              <Bar ref="outcomeChartRef" :data="outcomeBreakdownChartData" :options="stackedOutcomeChartOptions" />
            </div>
            <p v-else class="empty-state">No sessions match the current filters.</p>
          </article>

          <article class="panel chart-card">
            <div class="card-header">
              <h3>Average Moves, Collisions, and Backtracks</h3>
              <button class="text-btn" @click="downloadChart(performanceChartRef, 'movement-metrics')">PNG</button>
            </div>
            <div v-if="hasFilteredData" class="chart-wrap">
              <Bar ref="performanceChartRef" :data="performanceChartData" :options="chartBaseOptions" />
            </div>
            <p v-else class="empty-state">No sessions match the current filters.</p>
          </article>

          <article class="panel chart-card">
            <div class="card-header">
              <h3>Cost and Tokens Trend Over Time</h3>
              <button class="text-btn" @click="downloadChart(trendChartRef, 'cost-token-trend')">PNG</button>
            </div>
            <div v-if="hasFilteredData" class="chart-wrap">
              <Line ref="trendChartRef" :data="trendChartData" :options="trendChartOptions" />
            </div>
            <p v-else class="empty-state">No sessions match the current filters.</p>
          </article>

          <article class="panel chart-card chart-card-wide">
            <div class="card-header">
              <h3>Goal Coordinates Impact</h3>
              <button class="text-btn" @click="downloadChart(goalImpactChartRef, 'goal-impact')">PNG</button>
            </div>
            <div v-if="hasFilteredData" class="chart-wrap">
              <Bar ref="goalImpactChartRef" :data="goalImpactChartData" :options="goalImpactChartOptions" />
            </div>
            <p v-else class="empty-state">No sessions match the current filters.</p>
          </article>
        </section>

        <article class="panel table-card">
          <div class="card-header">
            <h3>Session Table (Newest First)</h3>
          </div>

          <div class="table-wrap" v-if="filteredSessions.length > 0">
            <table>
              <thead>
                <tr>
                  <th>Time</th>
                  <th>Model</th>
                  <th>Outcome</th>
                  <th>Moves</th>
                  <th>Collisions</th>
                  <th>Backtracks</th>
                  <th>Tokens</th>
                  <th>Cost</th>
                  <th>Goal</th>
                  <th>Reasoning</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="session in filteredSessions" :key="session.sessionId + session.fileName">
                  <td>{{ formatDateTime(session.startTime) }}</td>
                  <td>{{ session.modelName }}</td>
                  <td>{{ formatOutcomeLabel(session.outcomeType) }}</td>
                  <td>{{ formatInteger(session.totalMoves) }}</td>
                  <td>{{ formatInteger(session.wallCollisions) }}</td>
                  <td>{{ formatInteger(session.backtracks) }}</td>
                  <td>{{ formatInteger(session.totalTokens) }}</td>
                  <td>{{ formatMoney(session.totalCostUsd, 4) }}</td>
                  <td>{{ formatGoalLabel(session.showGoalCoordinates) }}</td>
                  <td>{{ formatReasoningLabel(session.reasoningEnabled) }}</td>
                </tr>
              </tbody>
            </table>
          </div>

          <p v-else class="empty-state">No sessions match the current filters.</p>
        </article>
      </section>
    </main>
  </div>
</template>
