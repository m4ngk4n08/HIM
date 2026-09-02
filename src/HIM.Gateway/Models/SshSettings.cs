using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Models
{
    public class SshSettings
    {

    // ── Core ──────────────────────────────────────────────────────────
            public int Port { get; set; } = 2222;
            public string HostKeyPath { get; set; } = "hostkey.pem";
            public string WelcomeMessage { get; set; } = "--- Welcome to Angelo's Portfolio (SSH Edition) ---\n";
            public int MaxConnections { get; set; } = 20;

            // ── Idle / Session ────────────────────────────────────────────────
            /// <summary>Seconds before an idle session is forcefully terminated.</summary>
            public int IdleTimeoutSeconds { get; set; } = 300;

            // ── Per-IP Rate Limit (Layer 4) ───────────────────────────────────
            /// <summary>Rolling window in seconds for the per-IP connection rate limit.</summary>
            public int RateLimitWindowSeconds { get; set; } = 60;
            /// <summary>Max connection attempts per IP within RateLimitWindowSeconds.</summary>
            public int RateLimitMaxAttempts { get; set; } = 10;

            // ── Per-IP Concurrent Connections (Layer 5) ───────────────────────
            /// <summary>Max simultaneous active connections allowed per IP.</summary>
            public int MaxConcurrentPerIp { get; set; } = 3;

            // ── Ban Service (Layer 1) ─────────────────────────────────────────
            /// <summary>Number of rate-limit or concurrent violations before an IP is banned.</summary>
            public int BanThresholdStrikes { get; set; } = 3;
            /// <summary>
            /// Base ban duration in minutes. Escalates automatically:
            ///   Gen 1 → BanDurationMinutes × 1  (default: 10 min)
            ///   Gen 2 → BanDurationMinutes × 6  (default: 60 min / 1 hr)
            ///   Gen 3 → BanDurationMinutes × 144 (default: 1440 min / 24 hr)
            ///   Gen 4+ → BanDurationMinutes × 864 (default: 8640 min / ~6 days)
            /// </summary>
            public int BanDurationMinutes { get; set; } = 10;

            // ── Tarpit (Layer 2) ──────────────────────────────────────────────
            /// <summary>
            /// Milliseconds to hold a rejected socket open before closing.
            /// Wastes bot scanner resources without blocking the accept loop.
            /// </summary>
            public int TarpitDelayMs { get; set; } = 1500;

            // ── Global Flood Guard (Layer 3) ──────────────────────────────────
            /// <summary>
            /// Maximum new TCP connections admitted per second across ALL IPs.
            /// Protects against distributed floods where no single IP hits per-IP limits.
            /// </summary>
            public int MaxGlobalConnectionsPerSecond { get; set; } = 5;

            // ── AI Chat Session Budget (SEC-04) ────────────────────────────────
            /// <summary>
            /// Max AI chat queries allowed per SSH session. Keyed by UserSessionState (one
            /// instance per connection, from the per-session DI scope) rather than by IP - the
            /// AI service's own rate limiter partitions by remote IP, but every request it sees
            /// arrives from the gateway container's single address, so that limiter is
            /// effectively one global bucket. This budget is the real per-identity control.
            /// Once hit, the session is pointed at /menu and /stats instead of erroring.
            /// </summary>
            public int MaxAiQueriesPerSession { get; set; } = 30;
    }
}
