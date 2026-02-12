/* global Chart, Tabulator */

const OUTCOMES = [
  { key: "won", label: "Won" },
  { key: "max_moves", label: "Max moves" },
  { key: "loops", label: "Loops" },
  { key: "error", label: "Error" },
  { key: "stopped", label: "Stopped" },
  { key: "unknown", label: "Unknown" },
];

let allSessions = [];
let table;
let winRateChart;
let outcomeChart;

function $(id) {
  return document.getElementById(id);
}

function money(n) {
  return `$${(n || 0).toFixed(4)}`;
}

function pct(n) {
  return `${(n || 0).toFixed(1)}%`;
}

function safeDate(val) {
  if (!val) return "";
  const d = new Date(val);
  if (Number.isNaN(d.getTime())) return "";
  return d;
}

function outcomeLabel(o) {
  const found = OUTCOMES.find(x => x.key === o);
  return found ? found.label : o;
}

function selectedModels() {
  const sel = $("modelSelect");
  return Array.from(sel.selectedOptions).map(o => o.value);
}

function selectedOutcomes() {
  const checks = document.querySelectorAll("input[name='outcome']:checked");
  return Array.from(checks).map(c => c.value);
}

function applyFilters() {
  const models = selectedModels();
  const outcomes = selectedOutcomes();
  const goal = $("goalSelect").value;

  return allSessions.filter(s => {
    if (models.length > 0 && !models.includes(s.modelName)) return false;
    if (outcomes.length > 0 && !outcomes.includes(s.outcomeType)) return false;

    if (goal === "on" && s.showGoalCoordinates !== true) return false;
    if (goal === "off" && s.showGoalCoordinates !== false) return false;
    if (goal === "unknown" && s.showGoalCoordinates != null) return false;

    return true;
  });
}

function computeByModel(sessions) {
  const map = new Map();

  for (const s of sessions) {
    const key = s.modelName || "Unknown";
    if (!map.has(key)) {
      map.set(key, {
        model: key,
        runs: 0,
        wins: 0,
        totalMoves: 0,
        totalCost: 0,
        outcomes: Object.fromEntries(OUTCOMES.map(o => [o.key, 0])),
      });
    }
    const row = map.get(key);
    row.runs += 1;
    row.wins += s.won ? 1 : 0;
    row.totalMoves += s.totalMoves || 0;
    row.totalCost += Number(s.totalCostUsd || 0);
    row.outcomes[s.outcomeType || "unknown"] = (row.outcomes[s.outcomeType || "unknown"] || 0) + 1;
  }

  const rows = Array.from(map.values());
  rows.sort((a, b) => a.model.localeCompare(b.model));
  return rows;
}

function updateKpis(sessions) {
  const runs = sessions.length;
  const wins = sessions.filter(s => s.won).length;
  const winRate = runs > 0 ? (wins / runs) * 100 : 0;
  const avgMoves = runs > 0 ? sessions.reduce((a, s) => a + (s.totalMoves || 0), 0) / runs : 0;
  const avgCost = runs > 0 ? sessions.reduce((a, s) => a + Number(s.totalCostUsd || 0), 0) / runs : 0;

  $("kpiRuns").textContent = runs.toString();
  $("kpiWins").textContent = wins.toString();
  $("kpiWinRate").textContent = pct(winRate);
  $("kpiAvgMoves").textContent = avgMoves.toFixed(1);
  $("kpiAvgCost").textContent = money(avgCost);
}

function buildCharts(sessions) {
  const byModel = computeByModel(sessions);

  const labels = byModel.map(r => r.model);
  const winRates = byModel.map(r => (r.runs > 0 ? (r.wins / r.runs) * 100 : 0));

  if (!winRateChart) {
    winRateChart = new Chart($("winRateChart"), {
      type: "bar",
      data: {
        labels,
        datasets: [
          {
            label: "Win rate (%)",
            data: winRates,
            backgroundColor: "rgba(115, 210, 255, 0.55)",
            borderColor: "rgba(115, 210, 255, 0.95)",
            borderWidth: 1,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        scales: {
          y: {
            beginAtZero: true,
            max: 100,
            grid: { color: "rgba(255,255,255,0.10)" },
            ticks: { color: "rgba(255,255,255,0.75)" },
          },
          x: {
            grid: { display: false },
            ticks: { color: "rgba(255,255,255,0.75)" },
          },
        },
        plugins: {
          legend: { labels: { color: "rgba(255,255,255,0.85)" } },
          tooltip: { callbacks: { label: ctx => `${ctx.raw.toFixed(1)}%` } },
        },
      },
    });
  } else {
    winRateChart.data.labels = labels;
    winRateChart.data.datasets[0].data = winRates;
    winRateChart.update();
  }

  const datasetColors = {
    won: "rgba(123, 255, 181, 0.55)",
    max_moves: "rgba(255, 198, 102, 0.55)",
    loops: "rgba(255, 145, 145, 0.55)",
    error: "rgba(255, 95, 95, 0.55)",
    stopped: "rgba(200, 200, 200, 0.45)",
    unknown: "rgba(160, 160, 160, 0.35)",
  };

  const breakdownDatasets = OUTCOMES.map(o => ({
    label: o.label,
    data: byModel.map(r => r.outcomes[o.key] || 0),
    backgroundColor: datasetColors[o.key] || "rgba(255,255,255,0.25)",
    borderColor: "rgba(255,255,255,0.10)",
    borderWidth: 1,
    stack: "outcomes",
  }));

  if (!outcomeChart) {
    outcomeChart = new Chart($("outcomeChart"), {
      type: "bar",
      data: {
        labels,
        datasets: breakdownDatasets,
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        scales: {
          y: {
            beginAtZero: true,
            grid: { color: "rgba(255,255,255,0.10)" },
            ticks: { color: "rgba(255,255,255,0.75)" },
          },
          x: {
            grid: { display: false },
            ticks: { color: "rgba(255,255,255,0.75)" },
          },
        },
        plugins: {
          legend: { labels: { color: "rgba(255,255,255,0.85)" } },
          tooltip: {
            callbacks: {
              title: ctx => ctx[0]?.label || "",
              label: ctx => `${ctx.dataset.label}: ${ctx.raw}`,
            },
          },
        },
      },
    });
  } else {
    outcomeChart.data.labels = labels;
    outcomeChart.data.datasets = breakdownDatasets;
    outcomeChart.update();
  }
}

function ensureTable() {
  if (table) return;

  table = new Tabulator("#sessionsTable", {
    layout: "fitColumns",
    height: "420px",
    selectable: 1,
    initialSort: [{ column: "startTime", dir: "desc" }],
    columns: [
      {
        title: "Time",
        field: "startTime",
        sorter: "datetime",
        width: 160,
        formatter: cell => {
          const d = safeDate(cell.getValue());
          return d ? d.toLocaleString() : "";
        },
      },
      { title: "Model", field: "modelName", widthGrow: 2 },
      {
        title: "Outcome",
        field: "outcomeType",
        width: 120,
        formatter: cell => outcomeLabel(cell.getValue()),
      },
      { title: "Moves", field: "totalMoves", hozAlign: "right", width: 90 },
      { title: "Coll", field: "wallCollisions", hozAlign: "right", width: 80 },
      { title: "Back", field: "backtracks", hozAlign: "right", width: 80 },
      {
        title: "Tokens",
        field: "totalTokens",
        hozAlign: "right",
        width: 110,
        formatter: cell => (cell.getValue() || 0).toLocaleString(),
      },
      {
        title: "Cost",
        field: "totalCostUsd",
        hozAlign: "right",
        width: 110,
        formatter: cell => money(Number(cell.getValue() || 0)),
      },
      {
        title: "Goal",
        field: "showGoalCoordinates",
        width: 80,
        formatter: cell => {
          const v = cell.getValue();
          if (v === true) return "On";
          if (v === false) return "Off";
          if (v == null) return "Unknown";
          return "Unknown";
        },
      },
    ],
  });
}

function updateUi() {
  const filtered = applyFilters();
  updateKpis(filtered);
  buildCharts(filtered);
  ensureTable();
  table.setData(filtered);
}

function buildOutcomeChecks() {
  const root = $("outcomeChecks");
  root.innerHTML = "";

  for (const o of OUTCOMES) {
    const id = `outcome_${o.key}`;
    const wrap = document.createElement("label");
    wrap.className = "check";
    wrap.innerHTML = `
      <input type="checkbox" id="${id}" name="outcome" value="${o.key}" checked />
      <span>${o.label}</span>
    `;
    root.appendChild(wrap);
  }
}

function buildModelSelect(sessions) {
  const models = Array.from(new Set(sessions.map(s => s.modelName || "Unknown"))).sort((a, b) => a.localeCompare(b));
  const sel = $("modelSelect");
  sel.innerHTML = "";
  for (const m of models) {
    const opt = document.createElement("option");
    opt.value = m;
    opt.textContent = m;
    sel.appendChild(opt);
  }
}

async function loadSessions() {
  const res = await fetch("/api/sessions", { cache: "no-store" });
  if (!res.ok) {
    throw new Error(`Failed to load sessions: ${res.status}`);
  }
  const data = await res.json();
  data.sort((a, b) => (a.startTime < b.startTime ? 1 : -1));
  return data;
}

async function init() {
  buildOutcomeChecks();

  $("refreshBtn").addEventListener("click", async () => {
    allSessions = await loadSessions();
    buildModelSelect(allSessions);
    updateUi();
  });

  $("modelSelect").addEventListener("change", updateUi);
  $("goalSelect").addEventListener("change", updateUi);
  document.addEventListener("change", (e) => {
    if (e.target && e.target.name === "outcome") updateUi();
  });

  allSessions = await loadSessions();
  buildModelSelect(allSessions);
  updateUi();
}

init().catch(err => {
  // eslint-disable-next-line no-console
  console.error(err);
  document.body.innerHTML = `<pre style="padding:16px;color:white;">${err.message}</pre>`;
});
