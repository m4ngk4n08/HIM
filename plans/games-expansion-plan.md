# H.I.M. Game Engine Expansion Plan

## 🎯 Objective
To transform the current command-based interaction into a robust, pluggable **Terminal User Interface (TUI) Game Engine**. The engine must support high-performance, interactive games (like 2048) over a high-latency SSH connection while maintaining a minimal memory footprint.

## 🏗️ Architectural Pattern: Strategy & Factory
To avoid a monolithic `CommandService`, we will implement a decoupled architecture where the engine manages the lifecycle, and individual games manage the logic.

### 1. The `IGame` Interface (The Contract)
Every game implementation must satisfy this contract to be compatible with the engine.

```csharp
public interface IGame
{
    string Name { get; }
    string Description { get; }

    // Lifecycle Management
    Task InitializeAsync(CancellationToken ct);
    Task ExecuteAsync(IAnsiConsole console, Stream sshStream, CancellationToken ct);
    Task ShutdownAsync();
}
```

### 2. The `GameFactory` (The Registry)
A centralized factory that resolves `IGame` implementations via Dependency Injection. This allows adding new games by simply registering them in `ServiceExtensions.cs`.

### 3. Shared Core Services (The Engine)
To prevent code duplication and ensure a consistent "feel," we will extract cross-cutting concerns into specialized services:

* **`IGameInputService` (High Priority):** 
    * **Responsibility:** Man's the raw SSH `Stream`. 
    * **Key Feature:** Must handle **ANSI Escape Sequences** (e.g., Arrow keys, WASD) and convert them into high-level `GameInput` enums.
    * **Optimization:** Must use a reusable byte buffer to minimize GC pressure during high-frequency input (essential for 2048).
* **`IGameVisualService`:** 
    * **Responsibility:** Manages `Spectre.Console.Live` displays.
    * **Key Feature:** Provides standardized "Game Over" panels, "Level Up" animations, and consistent color palettes.
* **`IGameScoreService`:** 
    * **Responsibility:** Manages local persistence of high scores using a lightweight JSON provider.

---

## ⚡ Performance & Memory Mandates

Since this engine runs inside an SSH Gateway, we must adhere to strict engineering standards to prevent lag and memory leaks.

### 1. Zero-Allocation Input Loop
* **Constraint:** Do NOT instantiate new objects inside the input `while` loop.
* **Requirement:** Use `Span<byte>` or a pre-allocated `byte[]` buffer for all `stream.ReadAsync` operations.
* **Goal:** Zero Garbage Collection (GC) pauses during active gameplay.

### 2. Single-Stream Ownership
* **Constraint:** Only **one** service may own the `Stream` at any given time.
* **Requirement:** The `GameFactory` must ensure that when a game is active, the `CommandService` is completely suspended to prevent race conditions on the SSH channel.

### 3. Asynchronous Non-Blocking I/O
* **Constraint:** Never use `Thread.Sleep()` or blocking `.Result` calls.
* **Requirement:** All delays and input waits must use `await Task.Delay()` or `await stream.ReadAsync()` to keep the Gateway thread pool available for other SSH sessions.

---

## 🚀 Implementation Roadmap

### Phase 1: Core Engine (The Foundation)
1. Define `IGame` and `IGameFactory`.
2. Implement `IGameInputService` with support for **Arrow Keys** and **WASD**.
3. Refactor `GameCommandService` to act as the engine entry point.

### Phase 2: Tier 1 Games (Logic-Focused)
* **`RegexQuest`**: A pattern-matching game. Tests logic and speed.
* **`CodeDebugger`**: A "find the bug" simulator. High thematic value.

### Phase 3: Tier 2 Games (Latency/Input-Focused)
* **`TwentyFortyEight`**: A high-performance tile-sliding game. Requires optimized `Live` rendering and ultra-low-latency input.
* **`Minesweeper`**: A grid-based coordinate game.

---

## 🧪 Validation Strategy
* **Unit Tests:** Validate `GameInputService` by mocking the `Stream` with specific ANSI byte sequences.
* **Integration Tests:** Ensure `GameFactory` correctly resolves and injects dependencies.
* **Latency Audit:** Verify that input-to-render latency remains within acceptable limits for an SSH session.
