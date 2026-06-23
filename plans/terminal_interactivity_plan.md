# Terminal Interactivity — Senior-Level Implementation Plan

> **Scope**: `/menu`, `/stats`, `/matrix`, `/game` commands for HIM.Gateway
> **Stack confirmed**: .NET 10, Spectre.Console **0.56.0**, no new NuGet packages required
> **Audit date**: 2026-06-21

---

## Verified Constraints & Findings

Before planning, the following facts were confirmed from source inspection:

| Fact | Detail |
|---|---|
| Spectre.Console version | **0.56.0** — `SelectionPrompt<T>.ShowAsync(IAnsiConsole, CancellationToken)` confirmed |
| `BarChart.AddItem(string, double, Color?)` | confirmed signature |
| `InteractionSupport.Yes` | Already set in `ConsoleEngineService.CreateConsole()` — arrow keys will work |
| `ICommandService` signature | `ProcessCommandAsync(IAnsiConsole console, string command, CancellationToken ct)` |
| `IConsoleEngineService` signature | `HandleInteractionLoopAsync(IAnsiConsole console, Stream stream, CancellationToken ct)` — **stream is already available** |
| All services registered | `Singleton` lifetime in `ServiceExtensions.cs` — **important constraint for /matrix** |
| `PortfolioData` | `PersonalInfo`, `List<WorkExperience>`, `Dictionary<string, List<string>> TechnicalSkills`, `List<ProjectItem>` |
| No `Tour` service wired | `Models/Tour/` models exist but no service registered — not blocked |

> [!IMPORTANT]
> **Critical Singleton constraint**: `CommandService` is a `Singleton`. The `/matrix` raw-stream animation must **not** store any per-session state in the service itself. The `Stream` must be passed in per-call from the interaction loop.

---

## Architecture Decision: Stream Passthrough for `/matrix`

The `/matrix` animation needs direct access to the raw SSH `Stream` for ANSI cursor-positioning escape codes, because Spectre.Console is line-oriented and cannot do arbitrary cursor writes.

**Current `ICommandService` signature:**
```csharp
Task ProcessCommandAsync(IAnsiConsole console, string command, CancellationToken ct);
```

**Decision: Extend the interface, not break it.**

Add an overload that accepts the stream. Call it from `ConsoleEngineService.HandleInteractionLoopAsync`, which already holds the stream. All other commands continue using the no-stream path via `Stream.Null`.

```csharp
// ICommandService.cs — add this overload
Task ProcessCommandAsync(IAnsiConsole console, string command, Stream stream, CancellationToken ct);
```

`ConsoleEngineService.HandleInteractionLoopAsync` already receives `Stream stream` — this requires **zero changes** to `TuiEngine`, `SshServerListener`, or `Program.cs`.

---

## File Change Map

```
HIM.Gateway/
├── Services/SSH/Interfaces/
│   └── ICommandService.cs                   [MODIFY] — add stream overload
├── Services/SSH/
│   ├── CommandService.cs                    [MODIFY] — inject & dispatch 4 commands
│   └── ConsoleEngineService.cs              [MODIFY] — pass stream to new overload
├── Extensions/
│   └── ServiceExtensions.cs                 [MODIFY] — register 4 new singletons
└── Services/SSH/Commands/                   [NEW DIRECTORY]
    ├── MenuCommand.cs                       [NEW]
    ├── StatsCommand.cs                      [NEW]
    ├── MatrixCommand.cs                     [NEW]
    └── GameCommand.cs                       [NEW]
```

Extracting each feature into its own `Commands/` class (Single Responsibility) keeps `CommandService` as a thin router — it dispatches only, never implements.

---

## Step 1 — Refactor: Routing Infrastructure

### 1.1 — Modify `ICommandService.cs`

```csharp
namespace HIM.Gateway.Services.SSH.Interfaces
{
    public interface ICommandService
    {
        Task ProcessCommandAsync(IAnsiConsole console, string command, CancellationToken ct);

        /// <summary>
        /// Stream-aware overload for commands requiring direct ANSI cursor control (e.g. /matrix).
        /// </summary>
        Task ProcessCommandAsync(IAnsiConsole console, string command, Stream stream, CancellationToken ct);
    }
}
```

### 1.2 — Modify `ConsoleEngineService.HandleInteractionLoopAsync`

In the Enter key branch (line ~94), one line changes:

```csharp
// BEFORE
await _commandService.ProcessCommandAsync(console, command, ct);

// AFTER — stream is already in scope in this method
await _commandService.ProcessCommandAsync(console, command, stream, ct);
```

### 1.3 — Modify `CommandService.cs`

Make the no-stream overload delegate to the primary, and implement the primary as the dispatcher:

```csharp
// Old overload now delegates — backward compatible
public Task ProcessCommandAsync(IAnsiConsole console, string command, CancellationToken ct)
    => ProcessCommandAsync(console, command, Stream.Null, ct);

// Primary overload with full routing
public async Task ProcessCommandAsync(IAnsiConsole console, string command, Stream stream, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(command)) return;

    if (_data == null)
    {
        console.MarkupLine("[red]Error:[/] Knowledge base file not found or corrupted.");
        return;
    }

    var table = new Table();

    switch (command.ToLower())
    {
        case "/help":       ShowHelp(console, table); break;
        case "/projects":   ShowProjects(console, table); break;
        case "/about":      ShowAbout(console); break;
        case "/skills":     ShowSkills(console, table); break;
        case "/experience": ShowExperience(console); break;
        case "/clear":      console.Clear(); break;
        case "/menu":       await _menuCommand.ExecuteAsync(console, _data, ct); break;
        case "/stats":      await _statsCommand.ExecuteAsync(console, _data, ct); break;
        case "/matrix":     await _matrixCommand.ExecuteAsync(console, stream, ct); break;
        case "/game":       await _gameCommand.ExecuteAsync(console, ct); break;
        case "/exit":
            console.MarkupLine("[red]Closing connection... Goodbye![/]");
            throw new OperationCanceledException();
        default:
            if (IsRateLimited(console))
            {
                console.MarkupLine($"[yellow]![/] [grey]{Markup.Escape("Neural Link is cooling down.. please wait")}[/]");
                break;
            }
            await HandleAiChatAsync(console, command, ct);
            break;
    }
}
```

Inject via constructor:

```csharp
public CommandService(
    IAiClientService aiClientService,
    IOptions<KnowledgeBaseSettings> kbSettings,
    MenuCommand menuCommand,
    StatsCommand statsCommand,
    MatrixCommand matrixCommand,
    GameCommand gameCommand)
{
    // ... existing + assign 4 new fields
}
```

### 1.4 — Modify `ServiceExtensions.cs`

```csharp
services.AddSingleton<MenuCommand>();
services.AddSingleton<StatsCommand>();
services.AddSingleton<MatrixCommand>();
services.AddSingleton<GameCommand>();
```

---

## Step 2 — `/menu` Command

**File:** `Services/SSH/Commands/MenuCommand.cs`

### Verified API
`SelectionPrompt<T>.ShowAsync(IAnsiConsole console, CancellationToken ct)` — confirmed Spectre.Console 0.56.0.

### Design Notes

- **Separator row**: Spectre.Console 0.56.0 `SelectionPrompt` has no native disabled-item API. The separator string IS selectable. The `default: continue` in the switch re-renders the menu — no crash, no UX break.
- **"Back" UX**: After a section renders, `TextPrompt<string>.AllowEmpty().ShowAsync()` blocks for Enter before looping.
- **OperationCanceledException**: Caught locally — does NOT propagate to disconnect the session. Returning from this method brings the user back to the `>` prompt.

```csharp
internal sealed class MenuCommand
{
    private static readonly string[] MenuOptions =
    [
        "About Me",
        "Skills & Tech Stack",
        "Work Experience",
        "Projects",
        "Developer Stats",
        "─────────────────",
        "Exit"
    ];

    public async Task ExecuteAsync(IAnsiConsole console, PortfolioData data, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            console.Clear();
            RenderMenuHeader(console);

            string choice;
            try
            {
                choice = await new SelectionPrompt<string>()
                    .Title("[bold cyan]What would you like to explore?[/]")
                    .PageSize(10)
                    .HighlightStyle(Style.Parse("bold cyan on grey19"))
                    .AddChoices(MenuOptions)
                    .ShowAsync(console, ct);
            }
            catch (OperationCanceledException)
            {
                return; // Clean exit — returns to > prompt
            }

            switch (choice)
            {
                case "About Me":            ShowAbout(console, data); break;
                case "Skills & Tech Stack": ShowSkills(console, data); break;
                case "Work Experience":     ShowExperience(console, data); break;
                case "Projects":            ShowProjects(console, data); break;
                case "Developer Stats":     ShowStats(console, data); break;
                case "Exit":                return;
                default:                    continue; // Separator — just redraw
            }

            console.WriteLine();
            console.MarkupLine("[grey]Press [white]Enter[/] to return to menu...[/]");

            try
            {
                await new TextPrompt<string>(string.Empty)
                    .AllowEmpty()
                    .ShowAsync(console, ct);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    private static void RenderMenuHeader(IAnsiConsole console)
    {
        console.Write(new Rule("[bold cyan]H I M — Portfolio Navigator[/]").RuleStyle("cyan").Centered());
        console.WriteLine();
    }

    // ShowAbout, ShowSkills, ShowExperience, ShowProjects, ShowStats
    // — these are private static methods that replicate CommandService's render logic.
    // They receive PortfolioData so MenuCommand is self-contained with no circular dependency.
}
```

> [!NOTE]
> `ShowStats` inside `MenuCommand` calls the same static render methods as `StatsCommand`. Since both are `internal sealed` with no shared mutable state, this is safe. Alternatively, `StatsCommand` can be injected into `MenuCommand` to avoid the duplication. The simpler approach (static helper) is fine for this scale.

---

## Step 3 — `/stats` Command

**File:** `Services/SSH/Commands/StatsCommand.cs`

### Design Notes

- Returns `Task.CompletedTask` — no `await` needed; using `async` would generate a compiler warning.
- `EscapeMarkup()` applied to **every** value from `PortfolioData` before rendering.
- `SkillProfile` dictionary uses `StringComparer.OrdinalIgnoreCase` — keys from the JSON file may differ in casing.
- `BarChart.Width(60)` — capped below typical SSH terminal width of 80 to avoid line wrapping artifacts.

```csharp
internal sealed class StatsCommand
{
    private static readonly Dictionary<string, (double Score, Color Color)> SkillProfile =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["backend"]  = (92, Color.Cyan1),
        ["frontend"] = (75, Color.Green),
        ["devops"]   = (85, Color.Yellow),
        ["database"] = (80, Color.Blue),
        ["ai"]       = (70, Color.Magenta1),
        ["mobile"]   = (65, Color.Orange1),
    };

    private static (double Score, Color Color) GetProfile(string category)
        => SkillProfile.TryGetValue(category, out var p) ? p : (60, Color.White);

    public Task ExecuteAsync(IAnsiConsole console, PortfolioData data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        console.WriteLine();
        RenderIdentityCard(console, data.PersonalInfo);
        console.WriteLine();
        RenderSkillBars(console, data.TechnicalSkills);
        console.WriteLine();
        RenderProjectSummary(console, data.Projects);
        console.WriteLine();

        return Task.CompletedTask;
    }

    private static void RenderIdentityCard(IAnsiConsole console, PersonalInfo p)
    {
        var github = p.Contact?.GetValueOrDefault("github", "N/A") ?? "N/A";

        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn()
            .AddRow("[grey]ROLE     :[/]", $"[bold white]{p.Role.EscapeMarkup()}[/]")
            .AddRow("[grey]LOCATION :[/]", $"[white]{p.Location.EscapeMarkup()}[/]")
            .AddRow("[grey]GITHUB   :[/]", $"[blue]{github.EscapeMarkup()}[/]")
            .AddRow("[grey]STATUS   :[/]", "[bold green]ONLINE[/]");

        console.Write(new Panel(grid)
            .Header($"[bold cyan]  {p.Name.ToUpper().EscapeMarkup()}  [/]")
            .Border(BoxBorder.Double)
            .BorderColor(Color.Cyan1)
            .Expand());
    }

    private static void RenderSkillBars(IAnsiConsole console, Dictionary<string, List<string>> skills)
    {
        var chart = new BarChart()
            .Width(60)
            .Label("[bold yellow underline]SKILL PROFICIENCIES[/]");

        foreach (var (category, _) in skills)
        {
            var (score, color) = GetProfile(category);
            // Capitalize first letter only — matches dictionary key casing from JSON
            var label = category.Length > 0
                ? char.ToUpper(category[0]) + category[1..]
                : category;
            chart.AddItem(label, score, color);
        }

        console.Write(chart);
    }

    private static void RenderProjectSummary(IAnsiConsole console, List<ProjectItem> projects)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold white]PROJECT LOG[/]")
            .AddColumn("[yellow]Name[/]")
            .AddColumn("[yellow]Stack[/]")
            .AddColumn("[yellow]Status[/]");

        foreach (var proj in projects)
        {
            var statusMarkup = proj.Status.ToLowerInvariant() switch
            {
                "live" or "active" => $"[bold green]{proj.Status.EscapeMarkup()}[/]",
                "wip"              => $"[yellow]{proj.Status.EscapeMarkup()}[/]",
                _                  => $"[grey]{proj.Status.EscapeMarkup()}[/]"
            };

            table.AddRow(
                $"[bold]{proj.Name.EscapeMarkup()}[/]",
                proj.Stack.EscapeMarkup(),
                statusMarkup);
        }

        console.Write(table);
    }
}
```

---

## Step 4 — `/matrix` Command

**File:** `Services/SSH/Commands/MatrixCommand.cs`

### Why Raw Stream (Not Spectre)

Spectre.Console's `IAnsiConsole.Write()` is line-buffered. The matrix effect requires **per-character cursor positioning** (`ESC[row;colH`) mid-screen — incompatible with Spectre's rendering model. We write to the same underlying SSH stream.

### ANSI Codes Used (standard VT100)

| Sequence | Effect |
|---|---|
| `\x1b[2J` | Clear entire screen |
| `\x1b[H` | Cursor to home (row 1, col 1) |
| `\x1b[?25l` | Hide cursor |
| `\x1b[?25h` | Show cursor (MUST restore on exit) |
| `\x1b[{r};{c}H` | Absolute cursor position (1-indexed) |
| `\x1b[32;1m` | Bright green (rain head) |
| `\x1b[32;2m` | Dim green (rain trail) |
| `\x1b[0m` | Reset all attributes |

### "Any Key Exits" — Design Rationale

The matrix loop runs **synchronously from the perspective of `HandleInteractionLoopAsync`** — it holds the loop iteration. The `stream.ReadAsync` in the interaction loop is not running during the animation. Therefore:

- **Exit by keypress**: NOT directly possible within the matrix loop without a second concurrent read.  
- **Correct approach**: Use only the **15-second hard cap** (inner `CancellationTokenSource.CancelAfter`). When it fires, `OperationCanceledException` is caught, `finally` restores the terminal, and the outer loop continues normally. The user then types their next command.
- **Display a notice**: Write `"Animation ends in 15s..."` at the bottom row so users are not confused.

> [!CAUTION]
> Do NOT attempt a concurrent `stream.ReadAsync` inside `MatrixCommand`. The SSH stream is not concurrency-safe for simultaneous read+write without explicit synchronization. The 15s timeout is the correct, safe design.

### Implementation

```csharp
internal sealed class MatrixCommand
{
    private static readonly char[] GlyphSet =
        "abcdefghijklmnopqrstuvwxyz0123456789@#$%&*<>".ToCharArray();

    private const int FrameDelayMs  = 80;    // ~12 FPS — safe over SSH latency
    private const int MaxDurationMs = 15_000; // 15-second hard cap

    public async Task ExecuteAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
    {
        if (!stream.CanWrite)
        {
            console.MarkupLine("[red]Matrix animation requires a writable SSH stream.[/]");
            return;
        }

        var width    = Math.Max(console.Profile.Width,  20);
        var height   = Math.Max(console.Profile.Height - 2, 10); // -2 to reserve status row
        var encoding = Encoding.UTF8;
        var rng      = new Random();

        // Per-call local state — safe because MatrixCommand is a Singleton with no instance fields
        var columns    = new int[width];
        var speeds     = new int[width];
        var ticks      = new int[width];

        for (int c = 0; c < width; c++)
        {
            columns[c] = rng.Next(0, height);
            speeds[c]  = rng.Next(1, 4); // 1=fast, 3=slow
        }

        using var animCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        animCts.CancelAfter(MaxDurationMs);

        // --- Setup ---
        await WriteRawAsync(stream, encoding, "\x1b[?25l\x1b[2J", ct); // hide cursor, clear
        await WriteRawAsync(stream, encoding,
            $"\x1b[{height + 1};1H\x1b[0m\x1b[2m  [Animation: 15s | then type your next command]\x1b[0m", ct);

        var sb = new StringBuilder(width * 20);

        try
        {
            while (!animCts.Token.IsCancellationRequested)
            {
                sb.Clear();

                for (int col = 0; col < width; col++)
                {
                    ticks[col]++;
                    if (ticks[col] < speeds[col]) continue;
                    ticks[col] = 0;

                    int head  = columns[col];
                    int trail = head - 1;
                    int erase = trail - 4;

                    if (head >= 1 && head <= height)
                    {
                        char g = GlyphSet[rng.Next(GlyphSet.Length)];
                        sb.Append($"\x1b[{head};{col + 1}H\x1b[32;1m{g}\x1b[0m");
                    }

                    if (trail >= 1 && trail <= height)
                    {
                        char g = GlyphSet[rng.Next(GlyphSet.Length)];
                        sb.Append($"\x1b[{trail};{col + 1}H\x1b[32;2m{g}\x1b[0m");
                    }

                    if (erase >= 1 && erase <= height)
                    {
                        sb.Append($"\x1b[{erase};{col + 1}H ");
                    }

                    columns[col] = (head >= height) ? rng.Next(0, height / 3) : head + 1;
                }

                // Single flush per frame — minimizes SSH round trips
                await WriteRawAsync(stream, encoding, sb.ToString(), animCts.Token);
                await stream.FlushAsync(animCts.Token);
                await Task.Delay(FrameDelayMs, animCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected — 15s timeout fired
        }
        finally
        {
            // CancellationToken.None — MUST restore even if session is cancelling
            await WriteRawAsync(stream, encoding, "\x1b[?25h\x1b[0m\x1b[2J\x1b[H", CancellationToken.None);
            await stream.FlushAsync(CancellationToken.None);
        }
    }

    private static async Task WriteRawAsync(Stream stream, Encoding enc, string text, CancellationToken ct)
        => await stream.WriteAsync(enc.GetBytes(text), ct);
}
```

> [!IMPORTANT]
> The `finally` block uses `CancellationToken.None`. If the session token `ct` is already cancelled when the animation ends, using `ct` in the finally would throw and skip the cursor-restore write, leaving the user's SSH client with a hidden cursor. `CancellationToken.None` is the only correct choice here.

---

## Step 5 — `/game` Command

**File:** `Services/SSH/Commands/GameCommand.cs`

### Verified APIs
- `TextPrompt<string>.AllowEmpty().ShowAsync(IAnsiConsole, CancellationToken)` — confirmed 0.56.0
- `FigletText`, `Panel`, `Rule` — existing, already used in codebase

### Design Notes

- **`Func<string, bool> Judge`**: Flexible answer evaluation — accepts synonyms without hardcoded exact-match.
- **Easter eggs checked first**: Before correctness evaluation. Easter egg responses are themselves escaped with `Markup.Escape()` before rendering.
- **All user input escaped**: `Markup.Escape(raw)` before any `MarkupLine` call — prevents markup injection.
- **`ct.ThrowIfCancellationRequested()`** at start of each loop iteration — clean cancellation on SSH disconnect.

```csharp
internal sealed class GameCommand
{
    private sealed record TriviaQuestion(
        string Prompt,
        Func<string, bool> Judge,
        string Explanation);

    private static readonly Dictionary<string, string> EasterEggs =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["rm -rf /"]    = "Nice try. This is a sandboxed SSH session.",
        ["sudo"]        = "sudo: permission denied. You are 'explorer', not root.",
        ["hire me"]     = "DM received. Angelo will be in touch.",
        ["42"]          = "Correct in the cosmic sense. But not for this question.",
        ["blockchain"]  = "No. Just... no.",
        ["hello world"] = "A classic. Still wrong though.",
    };

    public async Task ExecuteAsync(IAnsiConsole console, CancellationToken ct)
    {
        RenderHeader(console);

        var questions = BuildQuestions();
        int score     = 0;
        int maxScore  = questions.Count * 10;

        for (int i = 0; i < questions.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var q = questions[i];
            console.WriteLine();
            console.MarkupLine($"[bold yellow]Q{i + 1}/{questions.Count}[/]  {q.Prompt}");

            string raw;
            try
            {
                raw = await new TextPrompt<string>("[grey]>[/]")
                    .AllowEmpty()
                    .ShowAsync(console, ct);
            }
            catch (OperationCanceledException) { return; }

            string trimmed = raw.Trim();

            // 1. Easter egg check (before judge)
            if (EasterEggs.TryGetValue(trimmed, out var egg))
            {
                console.MarkupLine($"[italic grey]{Markup.Escape(egg)}[/]");
                await Task.Delay(1500, ct);
                continue; // no score
            }

            // 2. Answer evaluation
            if (q.Judge(trimmed))
            {
                score += 10;
                console.MarkupLine("[bold green]  Correct! +10 XP[/]");
            }
            else
            {
                console.MarkupLine("[red]  Not quite.[/]");
                console.MarkupLine($"[grey]  {Markup.Escape(q.Explanation)}[/]");
            }

            await Task.Delay(900, ct);
        }

        RenderFinalScore(console, score, maxScore);
    }

    private static void RenderHeader(IAnsiConsole console)
    {
        console.WriteLine();
        console.Write(new Rule("[bold yellow]DEV TRIVIA[/]").RuleStyle("yellow"));
        console.MarkupLine("[grey]5 questions. Honest answers only.[/]");
        console.WriteLine();
    }

    private static void RenderFinalScore(IAnsiConsole console, int score, int max)
    {
        console.WriteLine();
        console.Write(new Rule("[bold cyan]RESULTS[/]").RuleStyle("cyan"));

        var rank = score switch
        {
            >= 50 => "[bold gold1]RANK: PRINCIPAL ENGINEER[/]",
            >= 40 => "[bold cyan]RANK: SENIOR DEV[/]",
            >= 30 => "[bold green]RANK: MID-LEVEL DEV[/]",
            >= 20 => "[bold yellow]RANK: JUNIOR DEV[/]",
            _     => "[bold red]RANK: INTERN[/]"
        };

        var grid = new Grid()
            .AddColumn().AddColumn()
            .AddRow("[grey]Score :[/]", $"[white]{score} / {max}[/]")
            .AddRow("[grey]Rank  :[/]", rank);

        console.Write(new Panel(grid)
            .Header("[bold cyan] GAME OVER [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1));
        console.WriteLine();
    }

    private static IReadOnlyList<TriviaQuestion> BuildQuestions() =>
    [
        new("What does the `async` keyword in C# enable?",
            a => a.Contains("non-block", StringComparison.OrdinalIgnoreCase)
              || a.Contains("asynchron", StringComparison.OrdinalIgnoreCase),
            "It enables non-blocking execution via the Task-based async pattern."),

        new("What HTTP status code means 'resource not found'?",
            a => a.Contains("404", StringComparison.Ordinal),
            "404 Not Found."),

        new("What does SOLID stand for in software design? (name any one principle)",
            a => a.Contains("single", StringComparison.OrdinalIgnoreCase)
              || a.Contains("open",   StringComparison.OrdinalIgnoreCase)
              || a.Contains("liskov", StringComparison.OrdinalIgnoreCase)
              || a.Contains("interface", StringComparison.OrdinalIgnoreCase)
              || a.Contains("dependency", StringComparison.OrdinalIgnoreCase),
            "Single Responsibility, Open/Closed, Liskov Substitution, Interface Segregation, Dependency Inversion."),

        new("What is the time complexity of a binary search?",
            a => a.Contains("log", StringComparison.OrdinalIgnoreCase)
              || a.Contains("O(log", StringComparison.OrdinalIgnoreCase),
            "O(log n) — halves the search space on each step."),

        new("In Docker, what does the EXPOSE instruction do?",
            a => a.Contains("document", StringComparison.OrdinalIgnoreCase)
              || a.Contains("declar",   StringComparison.OrdinalIgnoreCase)
              || a.Contains("metadata", StringComparison.OrdinalIgnoreCase),
            "EXPOSE is a documentation hint — it declares the port but does NOT publish it. Use -p to publish."),
    ];
}
```

---

## Step 6 — Update `ShowHelp` in `CommandService`

```csharp
table.AddRow("/menu",   "Interactive navigation menu (arrow keys)")
     .AddRow("/stats",  "Developer RPG stats sheet")
     .AddRow("/matrix", "Digital rain animation (15s cap)")
     .AddRow("/game",   "Developer trivia game");
```

---

## Implementation Order

| # | Task | Risk | Est. Time |
|---|---|---|---|
| 1 | Interface + routing refactor (Steps 1.1–1.4) | Low | 30 min |
| 2 | `/stats` (`StatsCommand.cs`) | None — pure Spectre | 45 min |
| 3 | `/menu` (`MenuCommand.cs`) | Low — SSH arrow key test needed | 45 min |
| 4 | `/game` (`GameCommand.cs`) | Low | 1 hr |
| 5 | `/matrix` (`MatrixCommand.cs`) | Medium — raw ANSI + stream | 1.5 hr |

> [!TIP]
> Start with `/stats`. It requires no interface changes, no new wiring, no async Spectre prompts — just a new class and a `case` in the switch. It validates the `Commands/` pattern with zero risk before the harder features.

---

## Verification Plan

| Command | Test Step | Pass Criteria |
|---|---|---|
| `/stats` | Type `/stats` | Identity card, bar chart, project table render; no markup exception |
| `/stats` | Unknown skill category in KB | Falls back to `(60, Color.White)` — no crash |
| `/menu` | Type `/menu`, use arrow keys | Selection list navigates; correct section renders on Enter |
| `/menu` | Select "Exit" from menu | Returns to `>` prompt; session not disconnected |
| `/menu` | Select separator row | Menu re-renders cleanly (no crash) |
| `/game` | Type `/game`, enter "rm -rf /" | Easter egg fires; question skipped; no score |
| `/game` | Complete all 5 questions | Final score panel + rank renders correctly |
| `/game` | Type `[bold]` as answer | Rendered escaped — no markup injection |
| `/matrix` | Type `/matrix` | Green rain fills screen; screen restored after 15s |
| `/matrix` | Disconnect during animation | `finally` block executes; server does not throw |
| **Regression** | All original commands | `/about`, `/skills`, `/projects`, `/experience`, `/clear`, `/exit`, AI chat all unchanged |

---

## What Is Explicitly NOT Changing

| File | Reason |
|---|---|
| `TuiEngine.cs` | No changes — session lifecycle untouched |
| `SshServerListener.cs` | No changes — TCP/SSH layer untouched |
| `ConsoleEngineService.CreateConsole()` | No changes — `InteractionSupport.Yes` already set |
| `Program.cs` | No changes — only `ServiceExtensions.cs` gets 4 new lines |
| `appsettings.json` | No changes |
| `docker-compose.yml` | No changes |
| NuGet packages | No new packages — Spectre.Console 0.56.0 has everything needed |
