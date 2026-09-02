# CLAUDE.md

Guidance for Claude Code working in this repository.

## What this is

HIM ("Heuristic Interactive Mockup") is an interactive portfolio delivered over SSH: visitors
run `ssh angelodavales.info` and land in a Spectre.Console TUI backed by a RAG chat service.
Two .NET 10 services, deployed as containers to a single VPS.

- `src/HIM.Gateway` — console app. Custom SSH server (`Microsoft.DevTunnels.Ssh`), the accept-loop
  defense pipeline, session lifecycle, TUI rendering, command dispatch, and the pluggable game engine.
  Listens on port 22 in production.
- `src/HIM.AiService` — ASP.NET Core service (controllers). Manual RAG pipeline: in-process ONNX
  `all-minilm-l6-v2` embeddings (no external embedding call), SIMD vector search, Gemini for chat.
  Bound to `127.0.0.1:8080` — never exposed publicly; the gateway reaches it over the compose bridge.
- `tests/HIM.Gateway.Tests`, `tests/HIM.AiService.Tests` — xUnit. Both source projects grant
  `InternalsVisibleTo` to their test project, so testing `internal` members is the intended path.

The `plans/` directory is the project's working memory. **`plans/HANDOFF.md` is the entry point** —
read it first in a new session for current state, what is committed vs. pushed vs. deployed, and
the reasoning behind recent decisions. Individual task briefs live alongside it.

`plans/` is **git-ignored on purpose**, so it exists only in a local working copy — a fresh clone
has no `plans/` at all. If it is missing, nothing is broken and nothing was lost; ask the user for
the handoff rather than assuming the project has no history.

## Working agreement

Two models work in this repo. Sonnet implements a brief from `plans/`; Opus writes the briefs and
reviews the result. If you are Opus, your default job is review, not typing the code yourself.

### No deviation

- The brief is the scope. Do all of it, and nothing it didn't ask for.
- If a brief looks wrong, say so in a sentence, then keep going under a stated assumption. Don't
  silently change the plan.
- Standing ground rules the briefs assume: match each file's existing BOM and line endings exactly
  (some files have a BOM, some don't — match HEAD per file); mutation-verify every new test (break
  the code on purpose, confirm the right test fails and only that one, restore); run
  `dotnet test HIM.slnx` in parallel, because a serial pass has hidden a real fixture race here
  before; one commit per lettered item.
- "Refactor" means behavior does not change — same decisions, same order, same log strings. If you
  catch yourself improving something the brief didn't ask about, stop and write it down instead.
- Don't push or deploy without asking.

### No hallucination

- Verify before asserting. Read the file, run the test, check `git log`. "I believe" is not a finding.
- Never say a test passed, a build succeeded, or a file exists unless you ran or read it this
  session. If something failed, say so and paste the actual output.
- Cite real symbols and line numbers. If you can't find something, say "I couldn't find it" — do not
  invent a plausible-looking path, flag, or API.
- Don't take a subagent's or another model's report at face value. Spot-check its claims against the
  code before repeating them.
- "I don't know" is an acceptable answer. Say what you're unsure of and what would settle it.

### Reviewing Sonnet's work (Opus)

Review like a senior principal engineer: skeptical about claims, specific about evidence, decent
about how you say it.

- Start from the brief. Did it do everything asked, and only that? List missed items and
  unrequested extras separately — both are deviations.
- Verify the write-up instead of believing it. Re-run the tests, read the diff, confirm the
  mutation-verification actually happened rather than being asserted.
- Re-check the traps in "Non-obvious constraints" every single review: log reason strings,
  sanitization of network input, gate order, `TimeProvider`, DI lifetimes, host key mount.
- Ask "what breaks in production," not "what's ugly." Rank findings by consequence and keep
  must-fix separate from taste.
- Give a plain verdict: what's solid, what must change before this ships, what you'd leave alone.
  Plain English, per the section below.
- Approving something you didn't actually check is worse than skipping the review.

## How to explain things here

Write for someone who can code but does not know this codebase or the security jargon around it.
Plain English. This applies to code reviews, task write-ups, commit bodies, and answers in chat.

- Say what happens, not the label for it. "Holds the connection open for 1.5 seconds so the bot
  wastes its time" beats "bounded tarpit."
- The first time a term appears, define it in half a sentence — "a token bucket (a counter that
  refills over time)". After that, use it freely.
- **Reviews:** lead with the plain consequence — "this lets one IP open unlimited connections" —
  then the code detail underneath. Don't open with a category name like "concurrency defect."
- **Tasks:** say what changed, what it does now that it didn't before, and what could break. Skip
  the guided tour of the code.
- If something is genuinely hard, explain it like I'm five: one short concrete analogy, then the
  real mechanism. Don't stop at the analogy — the real mechanism is the point.
- Don't reach for jargon to sound precise when the plain word is just as precise.

## Token discipline

Context is a budget. Spend it on detail that changes a decision, not on volume. Being terse in a
way that hides a real caveat costs more than it saves — so be brief where brevity is free (status,
confirmations) and detailed where the detail is load-bearing (why a change is safe, what a failure
would look like in production).

- Read narrowly first: grep for the symbol, then read the ~40 lines around the hit. Read a whole
  file only when the whole shape actually matters.
- Don't re-read a file right after editing it just to confirm the edit — the edit tool fails loudly
  if it didn't apply.
- Don't re-derive what the conversation already established, or what `plans/HANDOFF.md` already says.
- Quote the three lines that matter, not the sixty-line block around them.
- Batch: one search across the tree beats five per-file round trips, and independent commands can
  go in one message.
- Never search or read `bin/`, `obj/`, `.vs/`, `Logs/`, `cache/`, or the ONNX model files. They are
  large and never the answer.

## Build, test, run

```bash
dotnet build HIM.slnx      # what CI runs
dotnet test  HIM.slnx      # what CI runs
```

CI (`.github/workflows/test.yml`) runs both on every push and PR, with no secrets — keep it that way:
nothing in the build or test path may require an API key or an `appsettings.json`.

Local run: **run each service from its own project directory**, not the repo root.

```bash
cd src/HIM.AiService && dotnet run
cd src/HIM.Gateway   && dotnet run
```

`appsettings.json` loads relative to `ContentRoot`; from the repo root it silently falls back to
class defaults, which looks like working code with wrong behavior. `run-local.ps1` does a full
Docker teardown/rebuild/launch and connects on port 2222.

## Configuration

`appsettings.json` is git-ignored on purpose. Copy `appsettings.Template.json` next to it in each
project and fill it in, or use `dotnet user-secrets` from the project directory. The gateway's
`launchSettings.json` sets `DOTNET_ENVIRONMENT=Development`, which is what makes user-secrets load.

`AiServiceSettings:SharedSecret` (Gateway) and `AiSettings:Security:SharedSecret` (AiService) must
match. A mismatch does not fail startup — every AI call just 401s, which is a miserable debug.

Config precedence is `CreateApplicationBuilder`'s default chain (appsettings → environment file →
user secrets → env vars → CLI). **Do not re-add `appsettings.json` to the builder**; that appends it
after user secrets and overrides them, defeating the point. Program.cs carries a comment saying so.

## Non-obvious constraints

These have bitten before; check them before changing related code.

- **Log reason strings are a production interface.** Fail2Ban on the VPS parses the gateway's
  container logs and bans IPs on the reason strings (`"GlobalFloodLimit"`, `"Banned"`,
  `"RateOrConcurrentLimit"`). Renaming one silently changes who gets banned. `PerIpRateGate` (L4)
  and `PerIpConcurrencyGate` (L5) share `"RateOrConcurrentLimit"` deliberately.
- **Everything network-derived is stripped of control characters before logging.** SSH usernames
  and channel request types go through `SshServerListener.SanitizeLogInput`, which truncates and
  removes CR/LF and ANSI escapes — otherwise a bot can forge whole log lines and steer Fail2Ban into
  banning someone else. New log statements over untrusted input must do the same. Note the method is
  currently `private` to `SshServerListener`; using it from another class means promoting or moving
  it first, not reaching for something else.
  **`SanitizerExtension` is not that tool** and will not close this hole. It redacts emails
  (`Redact`) and phone numbers (`Redact`, `RedactPhone`) for privacy at the egress and logging
  boundaries; it does not touch CR/LF or escape sequences. The two jobs are unrelated — do not
  substitute one for the other on the strength of the name.
- **Connection-gate registration order is evaluation order.** In `ServiceExtensions.AddService`:
  `GlobalFloodGate` (L3) → `IpBanGate` (L1) → `PerIpRateGate` (L4) → `PerIpConcurrencyGate` (L5),
  pinned by `ConnectionGatePipelineTests`. L4 enqueues its history entry *before* L5's concurrency
  check; that ordering is contractual, not incidental.
- **Not every defense layer is a gate.** L2 (tarpit) is the shared rejection action every gate feeds
  into; L6/L7 are per-session cancellation tokens; L8 is a keyboard-driven idle timer inside the TUI.
  Wrapping those in `IConnectionGate` would be dishonest — they have no decision to make at accept time.
- **Time is injected via `TimeProvider`.** Tests use `FakeTimeProvider`
  (`Microsoft.Extensions.TimeProvider.Testing`). New time-dependent code takes `TimeProvider`; tests
  must not use `Task.Delay`/`Thread.Sleep` to advance a window.
- **DI lifetimes are validated at startup.** `ContainerValidationOptions` turns on `ValidateScopes`
  and `ValidateOnBuild`, and `ServiceLifetimeTests` guards it. A scoped service resolved from the root
  provider would pin one visitor's session state process-wide, so a lifetime mistake fails the build,
  not production.
- **The host key is bind-mounted as a file** (`./keys/hostkey.pem:/app/hostkey.pem`), not a directory,
  so the SSH fingerprint survives redeploys. Changing that remounts as a fresh key and every returning
  visitor gets a host-key-mismatch warning.

## Conventions

- .NET 10 (C# 14), nullable and implicit usings enabled in every project.
- File-scoped or block namespaces both appear; match the file you are editing.
- Interfaces live in an `Interfaces/` (gateway) or `Interface/` (AI service) folder mirroring the
  implementation's folder, prefixed `I`.
- Comments here explain *why*, often citing the task or bug ID that motivated the code
  (`SEC-06`, `BL-2`, `16E`). Preserve them; they are the record of what a "cleanup" would break.
- Tests are named `Behavior_UnderCondition_Expectation` and assert on the exact reason string or
  observable effect, not on internals.

## Git

`main` is the working branch and is often ahead of `origin/main`; local commits are not necessarily
pushed, and pushed commits are not necessarily deployed (deploy is a separate
`gh workflow run deploy.yml`). `plans/HANDOFF.md` tracks which is which — check it before assuming
production runs what you see, and confirm with the user before pushing or deploying.

The untracked `HIM.Gateway/` and `HIM.Microservices/` directories at the repo root are stale build
leftovers, not source. The real projects are under `src/`.
