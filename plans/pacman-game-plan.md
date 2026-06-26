# Pac-Man Implementation Plan

## Goal
Implement a functional, ASCII-based Pac-Man game within `HIM.Gateway`, following the existing game infrastructure.

## Technical Design

### 1. `PacManGame : IGameService`
- **Core Loop:** Use a game loop that updates the screen based on user input (WASD).
- **Entities:**
    - Pac-Man (Player)
    - Ghosts (Simple pathfinding or random movement)
    - Pellets (Food)
- **Visuals:** Use `IGameVisualService` to render the grid-based map using characters (`@`, `P`, `#`, `.`, `o`).
- **Input:** Use `IGameInputService` to capture character input without pressing enter if possible (may need to investigate how `TriviaGame` handles input, it seems to wait for enter via `ReadInputManualAsync`). *Correction:* The `TriviaGame` waits for input via `_commandDispatcherHelper.ReadInputManualAsync`. For a real-time game like Pac-Man, this will be challenging in an SSH console. I may need to look into how to handle raw input over SSH.

### 2. Infrastructure Changes
- **Map Representation:** A 2D array or list of strings to represent the level.
- **Game Logic:**
    - Player movement validation.
    - Ghost AI (start simple: random move if not blocked).
    - Score tracking (`IGameScoreService`).

### 3. Challenges
- **Real-time Interaction:** The current game structure (Trivia) is turn-based. Pac-Man requires a real-time loop. I'll need to figure out how to capture non-blocking input over SSH.

## Execution Plan
1. **Research Input:** Investigate `HIM.Gateway/Services/SSH/CommandService.cs` or similar to see if raw/non-blocking input is supported.
2. **Prototype:** Implement a minimal grid renderer and movement system.
3. **Ghost AI:** Implement basic ghost behavior.
4. **Integration:** Register the new game in `GameFactoryService`.
5. **Testing:** Verify the game loop, input handling, and score saving.
