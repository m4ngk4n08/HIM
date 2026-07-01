# HIM (Heuristic Interactive Mockup) - AI SSH Portfolio

HIM is an interactive portfolio experience delivered via the SSH protocol. It features a custom SSH gateway and a .NET 10 microservices architecture to provide an AI-powered terminal interface designed to be professional, technical, and witty.

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
- **High-Energy UX:** Created a transition system using `Spectre.Console.Live` for immersive, full-screen animations.
- **SIMD-Accelerated Math:** High-performance vector normalization and Dot Product calculations using `System.Numerics`, making vector search lightning fast.
- **Binary Embedding Cache:** Implemented a custom binary persistence layer for embeddings, reducing AI service startup time from seconds to milliseconds.
- **High-Context RAG Ingestion:** Developed a recursive JSON flattener that consolidates objects (like work experiences) into semantically rich sentences for better LLM accuracy.
- **Polished SSH UX:** Integrated synchronized spinners and a character-by-character "typing" delay to simulate a real terminal interaction.
- **Robust TUI Architecture:** Hardened with markup escaping and optimized HTTP streaming to prevent session crashes and handle network latency.
- **Production-Hardened SSH Architecture (Path B):** Separated VPS admin access onto hardened OpenSSH port `43829` (key-only, disabled passwords, legacy `ssh-rsa` compatibility overrides) while keeping the custom `.NET` gateway on port `22` for frictionless user connections (`ssh angelodavales.info`) [2, 3].
- **Kernel-Level Rate Limiting (nftables):** Deployed a native `nftables` stateful ruleset that filters incoming connections on port `22` and port `43829` [2]. It blocks attackers at Layer 3/4 (using 1-hour blocklists) before they can consume container connection slots, preventing connection-slot starvation [1].
- **Persistent Host Key Management:** Resolved the `.NET` container permission denied boundary and the C# `File.Exists` 0-byte file trap by implementing a direct file-level bind-mount for `/app/hostkey.pem`, stabilizing the host fingerprint permanently across continuous delivery cycles [1].
- **Docker-to-Host Security Bridging:** Implemented Fail2Ban integration with both host systemd-journald and container log streams, dynamically banning bots that trigger rate limits or malicious username requests.
- **Self-Healing Connection Pool:** Built-in automatic resource reclamation utilizing multi-tier disarmable timeouts, OS-level TCP Keep-Alives [2.2.1], and immediate session teardown upon rejected command requests.

## 🏗️ Architecture
1. **HIM.Gateway:** The SSH server entry point (Console .NET 10). Manages TUI rendering, session lifecycle, and the pluggable Game Engine.
2. **HIM.AiService:** A RAG-based microservice for intelligent responses, optimized with hardware acceleration.
3. **TUI Application:** The interactive frontend integrated directly into the Gateway's session handling.

                                  [ INTERNET ]
                                       |
                +----------------------+----------------------+
                |                                             |
                | (Standard SSH Connection)                   | (Hardened Admin Connection)
                v                                             v
        [ Host Port 22 ]                               [ Host Port 43829 ]
                |                                             |
                v (nftables: prerouting -190)                 v (nftables: prerouting -190)
         [ f2b-table: drop ]                           [ f2b-table: drop ]
                |                                             |
                v (Docker Bridge NAT)                         v
         [ Forward Hook ]                             [ Host INPUT Hook ]
                |                                             |
                v                                             v
    [ him-gateway-1 Container ]                     [ Host OS OpenSSH ]
     - Managed-code SSH Server                       - Key-Only Auth (No Passwords) [2]
     - 8-Layer Active Defense                        - Monitored by Fail2Ban [2]
                |
                v (Private Bridge network)
       [ him-ai Container ]
        - Bound to port 8080
        - Invisible to the public internet

## 🛡️ Security & Resilience Architecture

To operate safely on standard Port 22, the gateway incorporates a highly hardened, lock-free application pipeline designed to resist automated scanning, connection-flooding, and slow-loris attacks [1, 1.1.2]:

### 1. The 8-Layer Application Defense-in-Depth

| Layer | Component | Defensive Mechanics |
| :--- | :--- | :--- |
| **Layer 1** | **IP BanList** | Fast, lock-free `ConcurrentDictionary` read of active Fail2Ban blocks [1]. |
| **Layer 2** | **Bounded Tarpit** | Holds rejected sockets open for `1500ms`. Bounded to a maximum of 100 concurrent tasks to protect the .NET Thread Pool under volumetric floods. |
| **Layer 3** | **Global Flood Guard** | Lock-free, CAS-based sliding token bucket enforcing coarse server-wide rate limits. |
| **Layer 4** | **Per-IP Rate Limit** | Lock-free sliding-window tracking using `ConcurrentQueue<DateTime>` per IP, completely eliminating lock contention under load. |
| **Layer 5** | **Per-IP Concurrency** | Interlocked session tracking that rejects clients attempting to hold multiple active connections. |
| **Layer 6** | **Handshake Timeout** | Enforces a strict **15-second** disarmable cancellation token during the initial cryptographic negotiation. |
| **Layer 7** | **Pre-Shell Negotiation Timeout** | Enforces a **15-second** countdown post-negotiation. If a bot completes the handshake but fails to request a shell channel, it is forcibly disconnected [1.2.7]. |
| **Layer 8** | **Interactive Idle Timeout** | A keyboard-driven idle timer (set to **30 minutes**) managed inside the TUI engine that resets on every user keystroke. |

---

### 2. OS-Level Socket & Log Hardening

#### A. Platform-Independent Socket Keep-Alives
If a client drops offline silently (half-open connection), standard Linux kernel TCP timeouts can take up to 2 hours to reap the socket. We configure native TCP keep-alive parameters directly on accepted sockets [2.2.1]:
```csharp
socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 60); // 60s idle before first probe
socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10); // 10s between probes
socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 5); // 5 retries before drop
```
This reclaims dead connection slots in minutes, ensuring pool availability.

#### B. Log Injection Shield
Because Fail2Ban parses container log outputs to execute blocks [1], the logging pipeline is protected against Carriage Return/Line Feed (`\r\n`) and ANSI terminal escape code injection. All variables parsed directly from the network (e.g., Usernames and Request types) are strictly sanitized via regular expressions before entering the logger:
```csharp
var clean = Regex.Replace(truncated, @"[\p{C}\r\n]", string.Empty);
return Regex.Replace(clean, @"\x1B\[[^@-_]*[0-9a-zA-Z]", string.Empty);
```

#### C. Immediate Teardown on Rejections
When a bot successfully negotiates the SSH transport layer but attempts to run forbidden command channels (such as `Type: exec` requests) [1.1.3], the gateway immediately terminates the parent `SshSession` [3.1]. This terminates the TCP socket and returns the connection slot back to the semaphore within milliseconds.

---

## 🛠️ Tech Stack
- **Language:** C# (.NET 10)
- **SSH Protocol:** `Microsoft.DevTunnels.Ssh` (v3.9.3)
- **UI:** `Spectre.Console`
- **AI Orchestration:** Custom RAG orchestration (Manual pipeline).
- **LLM/Embeddings:** Llama3 & `all-minilm` (via Ollama).
- **Performance:** `System.Numerics` (SIMD) for vector operations.
- **Security:** `nftables` (kernel-level packet filtering), Fail2Ban (journald integration).

## 🛡️ Host Hardening & Production Setup
To run this application in a production Internet-facing environment, the host VPS must be configured to prevent brute-forcing and connection slot exhaustion. 

For a complete, in-depth architectural breakdown of our security controls—including Netfilter bypass mitigation and non-root volume bootstrapping—please refer to the [Security Architecture and Hardening Reference](https://github.com/m4ngk4n08/markdowns/blob/main/security-architecture.md).

### Quick Setup Steps

#### 1. Docker Daemon Logging Integration
By default, Docker uses the `json-file` logging driver, which isolates container console output from host monitoring tools. You must configure Docker to log directly to `journald` so that Fail2Ban can parse container events natively [1]:
1. Edit `/etc/docker/daemon.json` and append:
   ```json
   {
     "log-driver": "journald"
   }
   ```
2. Restart the Docker service:
   ```bash
   sudo systemctl restart docker
   ```