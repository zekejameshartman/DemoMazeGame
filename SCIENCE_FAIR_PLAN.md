# Science Fair Plan: AI Maze Navigation Project

## The Scientific Discovery (What You Found)

Your son has discovered something genuinely interesting: **AIs perform WORSE when given the goal location**. This happens because:

1. LLMs use "greedy" thinking - they try to move toward the goal even when the maze requires going away from it first
2. The maze has a deceptive structure where the path to the exit requires backtracking through corridors that go opposite the goal direction
3. When NOT told the goal, AIs explore more randomly and sometimes stumble onto the correct path
4. When told the goal is at (11,9), they fixate on moving east/south and get trapped in the (8,5)-(8,7) dead-end corridor

**This is a real finding about AI limitations** - LLMs don't have true spatial reasoning or planning ability.

---

## 1. Scientific Framework

### Research Question
"How does providing goal location information affect AI performance in maze navigation?"

### Hypothesis (Formulated from observations)
**Original expectation:** "If we tell the AI where the exit is located, it will solve the maze faster because it has more information."

**What actually happened:** The opposite - AIs got stuck MORE when told the goal location.

**Refined hypothesis to test:** "Providing explicit goal coordinates to AI models will decrease their maze-solving success rate because they will use greedy 'toward-goal' movement rather than systematic exploration."

### Variables
- **Independent Variable:** Information given to AI (goal coordinates ON vs OFF)
- **Dependent Variables:**
  - Success rate (did it solve the maze?)
  - Number of moves to solve/fail
  - Number of backtracks (revisiting cells)
  - Wall collision rate
- **Controlled Variables:**
  - Same maze layout
  - Same starting position
  - Same move limit (200)
  - Same revisit limit (10)

---

## 2. Data Collection Plan

### Recommended Test Matrix
Run each combination 5-10 times for statistical validity:

| Model | Goal Coords ON | Goal Coords OFF |
|-------|---------------|-----------------|
| Claude Haiku 4.5 | 5-10 runs | 5-10 runs |
| GPT-4o Mini | 5-10 runs | 5-10 runs |
| Gemini 2.0 Flash | 5-10 runs | 5-10 runs |

**Minimum:** 30 total runs (3 models x 2 conditions x 5 runs)
**Better:** 60 total runs (3 models x 2 conditions x 10 runs)

### Metrics to Record Per Run
From your existing session logs, extract:
- Model name
- Goal coordinates setting (on/off)
- Outcome (won/stopped/max moves/error)
- Total moves
- Unique positions visited
- Backtrack count
- Wall collisions
- Time to complete (if won)

### Data Summary Script (Optional Enhancement)
Could add a simple script to aggregate session JSON files into a summary CSV for easier analysis.

---

## 3. Lab Notebook Structure

For a first science fair, a simple bound composition notebook or printed document works. Sections:

### Cover Page
- Title: "Do AIs Think Spatially? Testing Language Models in Maze Navigation"
- Student name, date, fair name

### Table of Contents

### Section 1: Question & Background (1-2 pages)
- What made you curious about this?
- Brief explanation: What is an LLM? What is maze-solving?
- Your original expectation

### Section 2: Hypothesis (1/2 page)
- Clear statement of what you predicted
- Why you predicted it

### Section 3: Materials & Methods (1-2 pages)
- Hardware/software used
- List of AI models tested
- The maze diagram (include the ASCII art)
- Explanation of variables
- How runs were conducted

### Section 4: Raw Data (2-4 pages)
- Table of all runs with metrics
- Can reference printed session logs in appendix

### Section 5: Results & Analysis (2-3 pages)
- Summary statistics (averages, success rates)
- Charts/graphs comparing conditions
- The "bottleneck position" finding

### Section 6: Conclusion (1 page)
- Was hypothesis supported or refuted?
- What does this tell us about how AIs "think"?
- Limitations of the study
- Future questions

### Appendix
- Sample session logs
- Code snippets (optional)
- Maze diagram with path analysis

---

## 4. Display Board Layout

Standard tri-fold board (36" x 48") layout:

```
+------------------+------------------+------------------+
|                  |                  |                  |
|    QUESTION      |     TITLE        |   CONCLUSION     |
|    & HYPOTHESIS  |                  |                  |
|                  |  "Do AIs Think   |   - Hypothesis   |
|  - Research Q    |   Spatially?"    |     REFUTED      |
|  - What we       |                  |   - AIs don't    |
|    predicted     |  [Photo of       |     plan ahead   |
|                  |   maze game      |   - More info    |
|                  |   running]       |     = worse      |
|                  |                  |     performance  |
+------------------+------------------+------------------+
|                  |                  |                  |
|    BACKGROUND    |     RESULTS      |   WHAT THIS      |
|                  |                  |   MEANS          |
|  - What is an    |  [Bar chart:     |                  |
|    LLM?          |   Success rate   |  - LLMs use      |
|  - What is       |   with/without   |    "greedy"      |
|    spatial       |   goal coords]   |    thinking      |
|    reasoning?    |                  |  - They can't    |
|                  |  [Heat map of    |    truly plan    |
|    THE MAZE      |   where AIs      |  - Real-world    |
|   [Diagram]      |   get stuck]     |    implications  |
|                  |                  |                  |
+------------------+------------------+------------------+
|                  |                  |                  |
|    METHODS       |     DATA         |   FUTURE         |
|                  |                  |   QUESTIONS      |
|  - Models tested |  [Table of       |                  |
|  - Variables     |   run results]   |  - Would bigger  |
|  - # of runs     |                  |    models do     |
|                  |  Key stats:      |    better?       |
|  [Screenshots    |  - 0% success    |  - Different     |
|   of AI playing] |  - 48% backtrack |    maze shapes?  |
|                  |  - 76% stuck at  |  - Can we train  |
|                  |    same spot     |    AIs to plan?  |
+------------------+------------------+------------------+
```

### Visual Elements to Include
1. **The maze diagram** - color-coded showing the "trap" area
2. **Bar chart** - success rate with vs without goal coordinates
3. **Sample AI conversation** - showing the AI's reasoning when stuck
4. **Heat map** - positions most frequently visited (shows the trap)
5. **Photo/screenshot** - the program running

---

## 5. Key Talking Points for Judges

When presenting, your son should be able to explain:

1. **The surprising result:** "We expected giving the AI more information would help, but it actually made things worse."

2. **Why this happens:** "The AI sees the goal is east and south, so it keeps trying to go that way. But the maze requires going west and north first to find the real path. The AI can't plan ahead - it just tries to get closer each step."

3. **What this tells us about AI:** "Large Language Models are really good at language but they don't truly understand space. They can't visualize a maze and plan a route like humans can."

4. **The evidence:** "We ran X tests. With goal coordinates, success rate was Y%. Without goal coordinates, success rate was Z%."

---

## 6. Implementation Steps

### Phase 1: Data Collection (if needed)
- [ ] Run additional test sessions with goal coordinates OFF
- [ ] Ensure balanced runs across models and conditions
- [ ] Export session logs for analysis

### Phase 2: Data Analysis
- [ ] Create summary spreadsheet of all runs
- [ ] Calculate statistics (success rates, averages, etc.)
- [ ] Identify patterns in the data

### Phase 3: Notebook
- [ ] Write up each section
- [ ] Include data tables and observations
- [ ] Add maze diagrams and screenshots

### Phase 4: Display Board
- [ ] Create charts/graphs from data
- [ ] Print maze diagram with annotations
- [ ] Arrange on tri-fold board
- [ ] Practice presentation

---

## 7. Optional: Automated Data Summary

If desired, I can help create a simple script that reads all session JSON files and outputs a summary CSV with the key metrics, making it easier to create charts in Excel/Google Sheets.
