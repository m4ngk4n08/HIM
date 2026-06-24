# H.I.M. Game Engine: Phase 3 - The Final Bosses

## 🎯 Objective
To implement high-performance, real-time interactive games (2048 and Minesweeper) that maintain a fluid "native" feel over high-latency SSH connections while adhering to strict memory and security mandates.

## 🏗️ The Technical Challenge: Real-Time vs. Buffered I/O
Standard SSH interactions are line-buffered (waiting for `
`). Real-time games require **Immediate Mode Input**.
- **Requirement:** The engine must poll the raw SSH stream and react to single-byte inputs or ANSI escape sequences without waiting for a newline.
- **Approach:** Pivot from `ReadInputManualAsync` (line-based) to `IGameInputService.GetNextInputAsync` (byte-based).

---

## ⚡ Memory & Performance Mandates (Critical)
To prevent GC spikes and lag in a multi-user SSH Gateway, the following engineering standards are mandatory:

### 1. Zero-Allocation Input Loop
- **Constraint:** No object instantiation inside the game's main `while` loop.
- **Implementation:** 
    - Use `Span<byte>` or `ReadOnlySpan<byte>` for parsing ANSI sequences.
    - Use pre-allocated `byte[]` buffers for all `stream.ReadAsync` calls.
    - Avoid `string` concatenation in the render loop; use `StringBuilder` or pre-defined `Markup` constants.

### 2. Optimized Rendering (The "Diff" Approach)
- **Constraint:** Minimize the volume of data sent over the SSH tunnel to prevent buffer bloat.
- **Implementation:** 
    - Utilize `Spectre.Console.Live` to maintain a persistent UI.
    - Only update the specific parts of the grid that changed.
    - Avoid full-screen redraws unless absolutely necessary.

### 3. Thread-Safe State Management
- **Constraint:** Ensure game state transitions are atomic.
- **Implementation:** Use `Interlocked` operations or lightweight `lock` objects for shared state (e.g., updating high scores during a game).

---

## 🛡️ Security & Robustness Mandates
- **Input Sanitization:** All raw bytes from the SSH stream must be validated. Invalid ANSI sequences must be discarded immediately to prevent "terminal injection" attacks.
- **Resource Bounds:** Implement a maximum buffer size for input reading to prevent Memory Exhaustion attacks (DoS) via massive stream bursts.
- **Execution Timeouts:** Implement a "Heartbeat" or timeout for active games to ensure orphaned SSH sessions do not leave game loops running indefinitely in the background.

---

## 🚀 Implementation Roadmap

### Step 1: The Real-Time Foundation
- **Input Audit:** Refine `GameInputService` to ensure zero-blocking behavior.
- **Loop Pattern:** Implement a `ReactiveGameLoop` pattern:
  `Poll Input` $ightarrow$ `Update Logic` $ightarrow$ `Partial Render` $ightarrow$ `Task.Delay(TICK)`.

### Step 2: TwentyFortyEight (2048)
- **Logic:** Implement a 4x4 merge-and-slide algorithm.
- **Input:** Bind Arrow Keys and WASD to grid transformations.
- **UI:** High-performance grid rendering using `Spectre.Console.Live`.

### Step 3: Minesweeper
- **Grid Logic:** Procedural mine placement and recursive "flood-fill" uncovering.
- **Virtual Cursor:** Implement a coordinate-based highlight `[ ]` that the user moves via Arrow Keys.
- **Interaction:** `Enter` to uncover, `Space` to flag.

### Step 4: Latency & Stability Audit
- **Simulation:** Test games under simulated 200ms+ latency.
- **Optimization:** Fine-tune `Task.Delay` intervals and buffer sizes to eliminate "stuttering."
- **Validation:** Verify zero memory growth over 10+ minutes of continuous gameplay.

---

## 🧪 Validation Strategy
- **Unit Tests:** Validate merge logic (2048) and flood-fill (Minesweeper) with edge-case grids.
- **Memory Profiling:** Use `.NET Memory Profiler` or `dotnet-counters` to ensure zero allocations in the hot path.
- **Stress Test:** Run 5 concurrent games on a single Gateway instance to ensure no thread contention.
