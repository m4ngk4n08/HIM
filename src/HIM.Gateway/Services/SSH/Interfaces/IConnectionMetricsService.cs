using System.Collections.Generic;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    /// <summary>
    /// A point-in-time copy of the connection metrics counters - safe to render from without
    /// racing the accept loop that keeps updating the live counters underneath it.
    /// </summary>
    public sealed record ConnectionMetricsSnapshot(
        TimeSpan Uptime,
        long TotalEvaluated,
        long TotalAllowed,
        long TotalRejected,
        IReadOnlyList<(string Layer, long Rejected)> RejectionsPerLayer);

    /// <summary>
    /// Records what SshServerListener.EvaluateGates already decides on every accepted TCP
    /// connection, so the /defense panel has something to render. Deliberately knows nothing
    /// about individual gates beyond their Layer name - it is filled in from the choke point
    /// every gate result already flows through, not by changing the gate classes themselves.
    /// Singleton: one process-wide counter set, matching the gates it counts.
    /// </summary>
    public interface IConnectionMetricsService
    {
        /// <summary>Call once per connection that cleared every gate.</summary>
        void RecordAllowed();

        /// <summary>Call once per connection a gate rejected, naming the rejecting gate's Layer.</summary>
        void RecordRejected(string layer);

        ConnectionMetricsSnapshot GetSnapshot();
    }
}
