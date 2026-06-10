# HIM (Heuristic Interactive Mockup) - AI SSH Portfolio

HIM is a next-generation interactive portfolio experience delivered via the SSH protocol. It features a custom SSH gateway and a .NET 10 microservices architecture to provide an AI-powered terminal interface that feels "Gen Z" elegant—professional, technical, and witty.

## 🚦 Project Status: **Phase 4 (Integration & Personality) - IN PROGRESS**
- ✅ **Phase 1 (AI Service):** Completed. Manual RAG pipeline implemented with Llama3 and all-minilm.
- ✅ **Phase 2 (SSH Bridge):** Completed. Custom SSH gateway with host key management and session handling.
- ✅ **Phase 3 (Terminal UI):** Completed. Responsive TUI with Spectre.Console, featuring a splash screen, command processing, and live AI chat.
- 🚧 **Phase 4 (Integration & Personality):** In Progress. Finalizing the AI's "Gen Z" witty personality and seamless TUI-to-AI service integration.

## 🚀 Features
- **Custom SSH Gateway:** Built with `Microsoft.DevTunnels.Ssh`, providing a secure, sandbox environment.
- **Interactive TUI:** A rich terminal experience using `Spectre.Console` with custom command routing (`/projects`, `/skills`, `/experience`, etc.).
- **AI-Powered Chat:** A manually orchestrated RAG (Retrieval-Augmented Generation) pipeline using Llama3 (Ollama) to answer questions about the owner's experience and skills.
- **Clean Architecture:** Strictly following SOLID principles and modern .NET 10 patterns.

## 🏗️ Architecture
1. **HIM.Gateway:** The SSH server entry point (Console .NET 10). Manages TUI rendering and session lifecycle.
2. **HIM.AiService:** A RAG-based microservice for intelligent responses.
3. **TUI Application:** The interactive frontend integrated directly into the Gateway's session handling.

## 🛠️ Tech Stack
- **Language:** C# (.NET 10)
- **SSH Protocol:** `Microsoft.DevTunnels.Ssh` (v3.9.3)
- **UI:** `Spectre.Console`
- **AI Orchestration:** Semantic Kernel (V1.77.0) for prompt synthesis.
- **LLM/Embeddings:** Llama3 & `all-minilm` (via Ollama).
- **RAG:** Manual pipeline (Chunking -> Embedding -> Vector Search -> Synthesis).

## 🗺️ Roadmap
- [x] **Phase 1: AI Service & Knowledge Base**
    - [x] Setup .NET 10 AI Microservice.
    - [x] Implement Manual RAG Pipeline (Stable & High Performance).
    - [x] Integrate Llama3 for intelligent synthesis.
- [x] **Phase 2: The SSH Bridge (Gateway)**
    - [x] Setup `HIM.Gateway` and Host Key Management.
    - [x] Implement Guest Authentication Handshake.
    - [x] Handle PTY and Window Resize events.
- [x] **Phase 3: Terminal UI (TUI) Development**
    - [x] Splash Screen & ASCII Art.
    - [x] Responsive Command Processing (`/help`, `/projects`, etc.).
    - [x] Live character-by-character AI streaming.
- [ ] **Phase 4: Integration & Personality**
    - [x] Connect TUI to AI Service via HTTP.
    - [ ] Fine-tune "Gen Z" witty personality and prompt engineering.
    - [ ] Add Easter Eggs (e.g., `/matrix`).
- [ ] **Phase 5: Deployment & Security**
    - [ ] Dockerization and restricted shell hardening.
    - [ ] Rate limiting and security audits.

## 📄 License
MIT
