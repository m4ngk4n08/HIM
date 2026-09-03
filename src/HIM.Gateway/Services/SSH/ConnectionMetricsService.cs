using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
using System.Collections.Generic;
using System.Linq;

namespace HIM.Gateway.Services.SSH
{
    /// <summary>
    /// Task 23C: per-layer accept/reject counters, filled in from
    /// SshServerListener.EvaluateGates - the one choke point every gate result already flows
    /// through - so this needs zero changes to the four gate classes.
    ///
    /// Layer names come from the registered gates at construction time, not hardcoded, so the
    /// per-layer counters stay a pre-sized int[] (Interlocked.Increment, no lock, no
    /// ConcurrentDictionary write per connection) while still not baking in "L1"/"L3"/"L4"/"L5"
    /// as string literals - matching the standard GlobalFloodGate sets for the accept-path hot
    /// loop.
    /// </summary>
    public sealed class ConnectionMetricsService : IConnectionMetricsService
    {
        private readonly TimeProvider _timeProvider;
        private readonly long _startTimestamp;
        private readonly string[] _layers;
        private readonly Dictionary<string, int> _layerIndex;
        private readonly int[] _rejectedPerLayer;

        private long _totalEvaluated;
        private long _totalAllowed;
        private long _totalRejected;

        public ConnectionMetricsService(IEnumerable<IConnectionGate> gates, TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
            _startTimestamp = timeProvider.GetTimestamp();

            _layers = gates.Select(g => g.Layer).ToArray();
            _layerIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < _layers.Length; i++)
                _layerIndex[_layers[i]] = i;

            _rejectedPerLayer = new int[_layers.Length];
        }

        public void RecordAllowed()
        {
            Interlocked.Increment(ref _totalEvaluated);
            Interlocked.Increment(ref _totalAllowed);
        }

        public void RecordRejected(string layer)
        {
            Interlocked.Increment(ref _totalEvaluated);
            Interlocked.Increment(ref _totalRejected);

            // An unrecognized layer name (shouldn't happen - every rejecting gate's Layer was
            // read at construction) is counted in the totals above but has no per-layer slot to
            // bump; silently skipping it here is safer than throwing off the accept path.
            if (_layerIndex.TryGetValue(layer, out var index))
                Interlocked.Increment(ref _rejectedPerLayer[index]);
        }

        public ConnectionMetricsSnapshot GetSnapshot()
        {
            var rejections = new (string Layer, long Rejected)[_layers.Length];
            for (var i = 0; i < _layers.Length; i++)
                rejections[i] = (_layers[i], Volatile.Read(ref _rejectedPerLayer[i]));

            return new ConnectionMetricsSnapshot(
                Uptime: _timeProvider.GetElapsedTime(_startTimestamp),
                TotalEvaluated: Interlocked.Read(ref _totalEvaluated),
                TotalAllowed: Interlocked.Read(ref _totalAllowed),
                TotalRejected: Interlocked.Read(ref _totalRejected),
                RejectionsPerLayer: rejections);
        }
    }
}
