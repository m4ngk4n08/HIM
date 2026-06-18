# Step-by-Step Integration Plan: Guided Portfolio Tour

  ## Summary

  Integrate /tour as a clean feature slice inside HIM.Gateway. The goal is to add guided portfolio navigation without bloating
  CommandService, changing the SSH transport, or touching the AI microservice.

  The implementation order should be: models first, content provider second, session/state service third, renderer fourth, command
  integration last, then tests.

  ## Integration Steps

  1. Create Tour Models

  - Add a new gateway folder such as Features/Tour or Services/Tour.
  - Define these types:
      - TourMode: Quick, Recruiter, Engineer
      - TourNavigationAction: Start, Next, Back, Skip, Exit, JumpToStep
      - TourStep: title, body, optional table rows/items, footer hint
      - TourSession: selected mode, current step index, active flag
      - TourCommandResult: handled flag, suppress AI fallback flag

  2. Add Tour Interfaces

  - Add focused interfaces:
      - ITourService
      - ITourSessionStore
      - ITourContentProvider
      - ITourRenderer

  - Keep these interfaces small. CommandService should only ask, “Can tour handle this input?” and not know the internals.

  3. Implement Session Storage

  - Implement TourSessionStore using ConditionalWeakTable<IAnsiConsole, TourSession>.
  - This matches the current per-user cooldown style.
  - Each SSH console gets its own independent tour session.
  - Store only lightweight session data: mode, current step, active/inactive.

  4. Implement Tour Content Provider

  - Implement TourContentProvider using loaded PortfolioData.
  - Build deterministic step lists for each mode:
      - Quick: intro, top skills, featured project, contact
      - Recruiter: intro, experience, skills, projects, contact
      - Engineer: system overview, architecture, AI/RAG, performance, projects, contact

  - Do not call the AI service in v1.
  - Escape all dynamic knowledge-base strings before rendering.

  5. Implement Tour Renderer

  - Implement TourRenderer using Spectre.Console.
  - Render the current step as a clean terminal panel.
  - Include progress text like Step 2 of 5.
  - Include footer hints:
      - next
      - back
      - number shortcuts
      - /exit-tour

  - Use tables only when content benefits from structure, such as skills or projects.

  6. Implement Tour Service

  - TourService becomes the feature orchestrator.
  - Responsibilities:
      - parse /tour, /tour quick, /tour recruiter, /tour engineer
      - start the correct session
      - handle active-tour commands: next, n, back, b, skip, numbers, exit, /exit-tour
      - render the current step after each valid action
      - return “not handled” for normal user questions so AI chat still works

  - Keep command parsing simple and deterministic.

  7. Register Dependencies

  - Update HIM.Gateway/Extensions/ServiceExtensions.cs.
  - Register:
      - ITourService
      - ITourSessionStore
      - ITourContentProvider
      - ITourRenderer

  - Use singleton registrations, consistent with the current gateway services.
  - Ensure session state is per-console, not global-user-shared.

  8. Integrate With CommandService

  - Inject ITourService into CommandService.
  - At the start of ProcessCommandAsync, after validating the knowledge base, call tour handling first.
  - If tour returns Handled = true, stop processing.
  - If tour returns Handled = false, continue existing command switch.
  - Preserve existing behavior for:
      - /help
      - /projects
      - /about
      - /skills
      - /experience
      - /clear
      - /exit
      - AI fallback

  9. Update Help Output

  - Add tour commands to /help:
      - /tour
      - /tour recruiter
      - /tour engineer
      - /exit-tour

  - Mention next, back, and number shortcuts briefly.

  10. Add Tests

  - Test TourService:
      - starts each mode
      - advances with next
      - reverses with back
      - exits with /exit-tour
      - jumps with numeric input
      - ignores unrelated text during active tour

  - Test CommandService routing with mocks:
      - /tour engineer goes to tour service
  - Run the local stack.
  - Connect with:

  ssh guest@localhost -p 2222

  - Verify these flows:
      - /help
      - /tour
      - next
      - back
  ## Acceptance Criteria

  - /tour works without requiring the AI service.
  - Each SSH session has isolated tour state.
  - Normal commands and AI chat continue working.
  - /exit-tour exits only the tour.
  - /exit still closes the SSH session.
  - Tour logic is testable without SSH transport.
  - CommandService remains an orchestrator, not the owner of tour business logic.