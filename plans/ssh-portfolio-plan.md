# SSH Portfolio Chatbot: Design & Implementation Plan

## Roles & Responsibilities
- **User:** Primary developer. Responsible for implementing code, configuring services, and debugging based on guidance.
- **Gemini CLI:** Technical Lead / Code Reviewer. Responsible for providing code snippets, architectural patterns, step-by-step instructions, and reviewing user-submitted code.
- **IMPORTANT!!!!!** The User is the one who should code. Gemini CLI MUST NOT use file-writing tools to implement the codebase unless explicitly directed for a specific repair. Read these roles and responsibilities and follow instructions strictly.

The objective is to create a unique, interactive portfolio experience that departs from traditional web-based portfolios. By using **SSH (Secure Shell)**, we leverage a developer-centric protocol to provide a Terminal User Interface (TUI) that feels professional, technical, and "Gen Z" elegant. The core feature is an AI-powered chatbot that knows everything about your projects, experience, and personality.

## 1. Coding Standards & Architectural Mandates
To ensure enterprise-grade quality, the following standards are strictly enforced:
- **Strict SOLID Principles:**
    - *Single Responsibility:* Each class/service does one thing.
    - *Open/Closed:* Systems should be open for extension but closed for modification.
    - *Liskov Substitution:* Subtypes must be substitutable for their base types.
    - *Interface Segregation:* No client should be forced to depend on methods it does not use.
    - *Dependency Inversion:* Depend on abstractions, not concretions. Use Dependency Injection (DI) for all services.
- **Clean Architecture & Structure:** Maintain clear boundaries between Domain, Application, and Infrastructure layers. Code must be written with a high-level architectural mindset, ensuring clean structure and readability.
- **Zero Hardcoding:** No hardcoded strings for infrastructure, business constants, or configuration. All settings must reside in `appsettings.json`, constants classes, or Environment Variables.
- **Type Safety:** Use strong typing and avoid `dynamic` or `object` where possible.
- **Async/Await:** All I/O bound operations must be asynchronous.

## 2. High-Level Architecture
The system follows a tiered architecture to ensure scalability and separation of concerns:

1.  **Client Tier:** Any terminal emulator (VS Code terminal, iTerm2, Putty) connecting via `ssh`.
2.  **SSH Gatekeeper:** Handles the connection, authentication (none/guest), and PTY (Pseudo-Terminal) allocation.
3.  **TUI Application (.NET Console):** The interactive layer that renders the UI, handles user input, and communicates with services.
4.  **AI Microservice (.NET 10):** A separate service hosting the Llama3 model and Vector Database (RAG) for intelligent responses.

**Data Flow:**
`User Input (SSH)` -> `SSH Server` -> `TUI App` -> `AI Microservice` -> `Response` -> `ANSI Rendering` -> `User Screen`

## 3. Technology Stack
- **SSH Protocol:** `Microsoft.DevTunnels.Ssh` (Pure .NET 10 implementation for the Gateway).
- **UI Framework:** **Spectre.Console** (For rich text, tables, progress bars, and "live" AI streaming).
- **Backend:** .NET 10 Web API / Minimal API.
- **AI/LLM:** Llama3 (Ollama) + `all-minilm` (Dedicated Embedding model).
- **Storage:** Simple In-Memory Vector Store for the RAG system.

## 4. Implementation Plan

### Phase 1: AI Service & Knowledge Base (The "Brain") [COMPLETED]
*   **Step 1.1:** Setup the .NET 10 AI Microservice.
*   **Step 1.2:** Define the "Knowledge Base" (JSON containing experience, skills, and personality).
*   **Step 1.3: Implement the RAG pipeline (Manual & Stable):** [COMPLETED]
    *   **Chunking:** Extract logical, contextualized sections from the knowledge base.
    *   **Embeddings:** Use Ollama's `all-minilm` model via `HttpClient` for stable vector generation.
    *   **Vector Search:** Custom `VectorSearchService` using Cosine Similarity.
    *   **Orchestration:** Use Semantic Kernel (V1.77.0) for final prompt synthesis.
*   **Step 1.4:** Integrate Llama3 via API to synthesize answers based on retrieved context.

### Phase 2: The SSH Bridge (Gateway) [COMPLETED]
*   **Step 2.1:** Setup the .NET 10 `HIM.Gateway` project using `Microsoft.DevTunnels.Ssh`.
*   **Step 2.2:** Implement **IHostKeyService** for persistent RSA/Ed25519 host key management.
*   **Step 2.3:** Implement **IAuthenticationService** for "Guest" access handling.
*   **Step 2.4:** Handle **PTY (pty-req)** and **Window Resize (window-change)** events to support responsive terminal UIs.
*   **Step 2.5:** Implement the **Shell Bridge** to pipe terminal I/O to the future TUI application.

### Phase 3: Terminal UI (TUI) Development [COMPLETED]
*   **Step 3.1:** Implement a "Splash Screen" with ASCII art and a welcome message.
*   **Step 3.2:** Build the Chat Interface:
    *   A scrollable history area.
    *   A live-updating area for the AI's "thinking" status.
    *   Streamed output (character-by-character) for that "AI feel".
*   **Step 3.3:** Add "Static" Views: Command-based navigation (e.g., type `/projects` to see a table of work, `/contact` for info).

### Phase 4: Integration & Personality [IN PROGRESS]
*   **Step 4.1:** Connect the TUI App to the AI Microservice via HTTP or gRPC.
*   **Step 4.2:** Fine-tune the AI "System Prompt" to reflect your personality (e.g., professional but approachable, technical but witty).
*   **Step 4.3:** Add Easter Eggs (e.g., hidden commands like `/matrix` or `/neofetch`).

### Phase 5: Deployment & Security
*   **Step 5.1:** Dockerize both services (TUI App + AI Service).
*   **Step 5.2:** Setup a firewall to only allow SSH (Port 22 or a custom port like 2222).
*   **Step 5.3:** Monitoring: Log queries (anonymously) to see what users are asking your AI.

## 5. Verification & Testing
- **Connectivity Test:** Ensure multiple SSH clients can connect simultaneously.
- **AI Accuracy Test:** Ask specific questions (e.g., "What did you do at X company?") and verify the RAG system picks the right context.
- **UI Stress Test:** Rapidly resize the terminal to ensure the TUI redraws correctly.
- **Latency Check:** Ensure the AI response time is acceptable for a "live" chat experience.

## 6. Security Considerations
- **Command Injection:** Since this is a restricted shell, ensure the user cannot escape the TUI app to the underlying OS.
- **Rate Limiting:** Prevent AI cost/compute abuse by limiting queries per IP.
- **No-Auth Login:** Configure SSH to allow a 'guest' user with no password or any password.
