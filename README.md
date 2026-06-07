# HIM (Heuristic Interactive Mockup) - AI SSH Portfolio

HIM is a next-generation interactive portfolio experience delivered via the SSH protocol. It features a custom SSH gateway and a .NET 10 microservices architecture to provide an AI-powered terminal interface.

## 🚀 Features
- **Custom SSH Gateway:** Built with `Microsoft.DevTunnels.Ssh`, providing a secure, sandbox environment.
- **AI-Powered Chat:** A RAG (Retrieval-Augmented Generation) pipeline using Llama3 (Ollama) to answer questions about the owner's experience and skills.
- **TUI (Terminal User Interface):** An elegant, responsive terminal UI built with `Spectre.Console`.
- **Clean Architecture:** Strictly following SOLID principles and modern .NET 10 patterns.

## 🏗️ Architecture
1. **HIM.Gateway:** The SSH server entry point.
2. **HIM.AiService:** A RAG-based microservice for intelligent responses.
3. **TUI Application:** (Upcoming) The interactive frontend.

## 🛠️ Tech Stack
- **Language:** C# (.NET 10)
- **SSH Protocol:** Microsoft.DevTunnels.Ssh
- **UI:** Spectre.Console
- **AI Orchestration:** Semantic Kernel
- **LLM/Embeddings:** Llama3 & all-minilm (via Ollama)

## 🚦 Getting Started
(Detailed instructions will be added as Phase 2 completes)

## 📄 License
MIT
