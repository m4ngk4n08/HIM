# HIM (Heuristic Interactive Mockup) - AI SSH Portfolio

HIM is a next-generation interactive portfolio experience delivered via the SSH protocol. It features a custom SSH gateway and a .NET 10 microservices architecture to provide an AI-powered terminal interface that feels "Gen Z" elegant—professional, technical, and witty.

## 🚦 Project Status: **Phase 5 (Validation & Refinement) - IN PROGRESS**
- ✅ **Phase 1 (AI Service):** Completed. Manual RAG pipeline implemented with Llama3 and all-minilm.
- ✅ **Phase 2 (SSH Bridge):** Completed. Custom SSH gateway with host key management and session handling.
- ✅ **Phase 3 (Terminal UI):** Completed. Responsive TUI with Spectre.Console, featuring a splash screen, command processing, and live AI chat.
- ✅ **Phase 4 (Integration & Personality):** Completed. Optimized SIMD-powered RAG, binary caching, and polished TUI animations.
- 🚧 **Phase 5 (Validation & Refinement):** In Progress. Final stability checks, hardening, and deployment preparation.

## 🚀 Key Technical Achievements
- **Pluggable TUI Game Engine:** Developed a decoupled gaming framework using Strategy & Factory patterns, enabling real-time, interactive games (like Trivia, RegexQuest, and CodeDebugger) over SSH.
- **Zero-Allocation Input System:** Implemented a high-performance ANSI parsing service that converts raw SSH byte-streams into game inputs with near-zero GC pressure.
- **High-Energy UX:** Created a "Digital Surge" transition system using `Spectre.Console.Live` for immersive, full-screen animations.
- **SIMD-Accelerated Math:** High-performance vector normalization and Dot Product calculations using `System.Numerics`, making vector search lightning fast.
- **Binary Embedding Cache:** Implemented a custom binary persistence layer for embeddings, reducing AI service startup time from seconds to milliseconds.
- **High-Context RAG Ingestion:** Developed a recursive JSON flattener that consolidates objects (like work experiences) into semantically rich sentences for better LLM accuracy.
- **Polished SSH UX:** Integrated synchronized spinners and a smooth character-by-character "typing" delay to simulate a real terminal interaction.
- **Robust TUI Architecture:** Hardened with markup escaping and optimized HTTP streaming to prevent session crashes and handle network latency.

## 🏗️ Architecture
1. **HIM.Gateway:** The SSH server entry point (Console .NET 10). Manages TUI rendering, session lifecycle, and the pluggable Game Engine.
2. **HIM.AiService:** A RAG-based microservice for intelligent responses, optimized with hardware acceleration.
3. **TUI Application:** The interactive frontend integrated directly into the Gateway's session handling.

## 🛠️ Tech Stack
- **Language:** C# (.NET 10)
- **SSH Protocol:** `Microsoft.DevTunnels.Ssh` (v3.9.3)
- **UI:** `Spectre.Console`
- **AI Orchestration:** Custom RAG orchestration (Manual pipeline).
- **LLM/Embeddings:** Llama3 & `all-minilm` (via Ollama).
- **Performance:** `System.Numerics` (SIMD) for vector operations.

## 🗺️ Roadmap
- [x] **Phase 1: AI Service & Knowledge Base**
    - [x] Setup .NET 10 AI Microservice.
    - [x] Implement Manual RAG Pipeline.
- [x] **Phase 2: The SSH Bridge (Gateway)**
    - [x] Setup `HIM.Gateway` and Host Key Management.
    - [x] Implement Guest Authentication Handshake.
- [x] **Phase 3: Terminal UI (TUI) Development**
    - [x] Splash Screen & ASCII Art.
    - [x] Responsive Command Processing (`/help`, `/projects`, etc.).
- [x] **Phase 4: Integration & Personality**
    - [x] SIMD-optimized Vector Search.
    - [x] Binary Embedding Caching.
    - [x] "Typing" animation and synchronized TUI feedback.
    - [x] Refined "Gen Z" witty personality prompt engineering.
- [x] **Phase 5: TUI Game Engine Expansion**
    - [x] Pluggable Engine Architecture (Strategy/Factory).
    - [x] Tier 1 Logic Games: Trivia, RegexQuest, CodeDebugger.
    - [ ] Tier 2 Real-Time Games: 2048, Minesweeper (Phase 3 - Final Bosses).
- [ ] **Phase 6: Deployment & Security**
    - [x] Dockerization and restricted shell hardening.
    - [x] Rate limiting and security audits.
    - [x] Add Easter Eggs (e.g., `/matrix`).

## 📄 License
MIT
