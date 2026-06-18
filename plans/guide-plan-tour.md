# Guided Portfolio Tour Feature Plan

  ## Summary

  Add a clean, deterministic /tour feature to the SSH portfolio that guides visitors through curated portfolio sections without
  rewriting the whole command system. V1 will support audience modes, typed navigation, and numeric shortcuts while preserving the
  current AI chat and existing commands.

  Default modes:

  - /tour or /tour quick: short general walkthrough
  - /tour recruiter: experience, skills, projects, contact
  - /tour engineer: architecture, projects, stack, AI/RAG implementation, contact

  ## Key Changes

  - Add a focused tour slice inside HIM.Gateway, keeping responsibilities separate:
      - ITourService: application use case for starting, navigating, exiting, and rendering tour state
      - ITourSessionStore: per-console/session state storage, implemented with ConditionalWeakTable<IAnsiConsole, TourSession>
      - ITourContentProvider: builds tour steps from PortfolioData
      - ITourRenderer: renders Spectre.Console panels/tables for tour output

  - Add small domain/application types:
      - TourMode: Quick, Recruiter, Engineer
      - TourNavigationAction: Start, Next, Back, Skip, Exit, JumpToStep
      - TourSession: mode, current step index, started timestamp
      - TourStep: id, title, short body, optional rows/items, suggested next actions
      - TourCommandResult: whether the command was handled, whether to suppress AI fallback

  - Update CommandService only as an orchestrator:
      - Recognize /tour, /tour quick, /tour recruiter, /tour engineer, /exit-tour
      - While a tour is active, recognize next, n, back, b, skip, exit, /exit-tour, and numeric step shortcuts
      - If input is not a tour navigation command, fall through to existing command/AI behavior and keep the tour session active

  - Update /help to include:
      - /tour
      - /tour recruiter
      - /tour engineer
      - /exit-tour

  - Keep AI service unchanged for v1. Tour content is deterministic and rendered from the gateway knowledge base.

  ## Behavior

  - /tour displays a mode menu and starts Quick by default if no mode is provided.
  - /tour recruiter starts a recruiter-focused sequence:
      1. Intro
      2. Experience
      3. Skills
      4. Featured projects
      5. Contact

  - /tour engineer starts a technical sequence:
      1. System overview
      2. HIM architecture
      3. AI/RAG pipeline
      4. Performance optimizations
      5. Featured projects
      6. Contact

  - /tour quick starts a concise general sequence:
      1. Intro
      2. Top skills
      3. Featured project
      4. Contact

  - During an active tour:
      - next/n advances one step
      - back/b goes back one step
      - skip advances one step
      - a valid number jumps to that step
      - /exit-tour or exit ends only the tour, not the SSH session
      - /exit still closes the SSH session as it does today

  - Dynamic strings from knowledge-base.json must be escaped before Spectre markup rendering.

  ## Test Plan

  - Add a gateway unit test project if none exists, focused on tour application behavior rather than SSH transport.
  - Unit test TourContentProvider:
      - builds correct step counts for Quick, Recruiter, and Engineer
      - uses available PortfolioData without throwing when optional fields are missing

  - Unit test TourService:
      - starts each mode correctly
      - handles next, back, skip, exit, and numeric jumps
      - clamps navigation at first/last step
      - returns “not handled” for non-tour text so AI fallback still works

  - Integration-style command tests with mocked ITourService and IAiClientService:
      - /tour engineer is routed to tour service
      - /exit-tour exits tour but does not throw OperationCanceledException
      - /exit behavior remains unchanged
      - normal commands like /projects and /skills still work

  - Manual acceptance:
      - run the gateway locally
      - connect with ssh guest@localhost -p 2222
      - verify /tour, /tour recruiter, /tour engineer, numbered jumps, next, back, /exit-tour, /help, and normal AI chat during an
        active tour

  ## Assumptions

  - This will be implemented as a scoped clean architecture slice inside HIM.Gateway, not a full command-system rewrite.
  - V1 will not call the AI service for tour summaries; it will reuse existing portfolio data and current command/AI fallback.
  - Existing CommandService remains the command entry point, but new tour logic lives behind interfaces to keep it SOLID and
    testable.

  - No database, migration, API contract, or AI microservice change is required.