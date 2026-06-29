# HIM (Heuristic Interactive Mockup) - AI SSH Portfolio

HIM is a next-generation interactive portfolio experience delivered via the SSH protocol. It features a custom SSH gateway and a .NET 10 microservices architecture to provide an AI-powered terminal interface that feels "Gen Z" elegant—professional, technical, and witty.

## 🚦 Project Status: **Phase 6 (Deployment & Security) - COMPLETED**
- ✅ **Phase 1 (AI Service):** Completed. Manual RAG pipeline implemented with Llama3 and all-minilm.
- ✅ **Phase 2 (SSH Bridge):** Completed. Custom SSH gateway with host key management and session handling.
- ✅ **Phase 3 (Terminal UI):** Completed. Responsive TUI with Spectre.Console, featuring a splash screen, command processing, and live AI chat.
- ✅ **Phase 4 (Integration & Personality):** Completed. Optimized SIMD-powered RAG, binary caching, and polished TUI animations.
- ✅ **Phase 5 (Validation & Refinement):** Completed. Final stability checks, TUI game engine expansion, and automated error-handling routines.
- ✅ **Phase 6 (Deployment & Security):** Completed. Native nftables firewall integration, isolated multi-tier port management, Fail2Ban container log filtering, and permanent cryptographic host keys.

## 🚀 Key Technical Achievements
- **Pluggable TUI Game Engine:** Developed a decoupled gaming framework using Strategy & Factory patterns, enabling real-time, interactive games (like Trivia, RegexQuest, and CodeDebugger) over SSH.
- **Zero-Allocation Input System:** Implemented a high-performance ANSI parsing service that converts raw SSH byte-streams into game inputs with near-zero GC pressure.
- **High-Energy UX:** Created a "Digital Surge" transition system using `Spectre.Console.Live` for immersive, full-screen animations.
- **SIMD-Accelerated Math:** High-performance vector normalization and Dot Product calculations using `System.Numerics`, making vector search lightning fast.
- **Binary Embedding Cache:** Implemented a custom binary persistence layer for embeddings, reducing AI service startup time from seconds to milliseconds.
- **High-Context RAG Ingestion:** Developed a recursive JSON flattener that consolidates objects (like work experiences) into semantically rich sentences for better LLM accuracy.
- **Polished SSH UX:** Integrated synchronized spinners and a smooth character-by-character "typing" delay to simulate a real terminal interaction.
- **Robust TUI Architecture:** Hardened with markup escaping and optimized HTTP streaming to prevent session crashes and handle network latency.
- **Production-Hardened SSH Architecture (Path B):** Separated VPS admin access onto hardened OpenSSH port `43829` (key-only, disabled passwords, legacy `ssh-rsa` compatibility overrides) while keeping the custom `.NET` gateway on port `22` for frictionless user connections (`ssh angelodavales.info`).
- **Kernel-Level Rate Limiting (nftables):** Deployed a native `nftables` stateful ruleset that filters incoming connections on port `22` and port `43829`. It dynamically blocks attackers at Layer 3/4 (using 1-hour blocklists) before they can consume container connection slots, preventing connection-slot starvation.
- **Persistent Host Key Management:** Resolved the `.NET` container permission denied boundary and the C# `File.Exists` 0-byte file trap by implementing a direct file-level bind-mount for `/app/hostkey.pem`, stabilizing the host fingerprint permanently across continuous delivery cycles.
- **Docker-to-Host Security Bridging:** Implemented Fail2Ban integration with both host systemd-journald and container log streams, dynamically banning bots that trigger rate limits or malicious username requests.

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
- **Security:** `nftables` (kernel-level packet filtering), Fail2Ban (journald integration).

## 🛡️ Host Hardening & Production Setup
To run this application in a production Internet-facing environment, the host VPS must be configured as follows to prevent brute-forcing and connection slot exhaustion:

### 1. Docker Daemon Logging Integration
By default, Docker uses the `json-file` logging driver, which isolates container console output from host monitoring tools. You must configure Docker to log directly to `journald` so that Fail2Ban can parse container events natively:
1. Edit `/etc/docker/daemon.json` and append:
   ```json
   {
     "log-driver": "journald"
   }