using HIM.Gateway.Services.SSH.Interfaces;

namespace HIM.Gateway.Services.SSH
{
    /// <summary>
    /// Task 25's fix: one owner for a session's stream reads. Bytes pushed back here (because the
    /// outer loop read more than one line's worth in a single ReadAsync and a nested prompt needs
    /// the rest) are served to whichever reader asks next, before either one touches the socket
    /// again. Scoped per session - see ServiceExtensions.AddService - so this must never become a
    /// singleton, or one visitor's leftover keystrokes would leak into another's session.
    /// </summary>
    public class SessionByteReader : ISessionByteReader
    {
        private readonly List<byte> _pending = new();

        // True when the most recently delivered byte was a bare CR (0x0D) and its paired LF
        // (0x0A) hasn't been seen yet. A visitor pasting Windows-style "\r\n" line endings - the
        // realistic trigger this task is about - can have that pair split across two ReadAsync
        // calls, or straddle the boundary between what one reader consumed and what got pushed
        // back for the other. Tracking it here, once, means neither reader has to re-derive it,
        // and a pushed-back LF can never be replayed as a second, phantom blank line.
        private bool _swallowNextLf;

        public async Task<int> ReadAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (_pending.Count > 0)
            {
                // Bytes here were already filtered the first time they came off the stream -
                // filtering them again would be redundant, not wrong, but there is nothing left
                // to strip.
                int n = Math.Min(count, _pending.Count);
                _pending.CopyTo(0, buffer, offset, n);
                _pending.RemoveRange(0, n);
                return n;
            }

            while (true)
            {
                int n = await stream.ReadAsync(buffer, offset, count, ct);
                if (n <= 0) return n;

                int filtered = FilterCrLf(buffer, offset, n);
                if (filtered > 0) return filtered;

                // The entire read was the LF half of a CRLF pair whose CR arrived in an earlier
                // read - nothing new to hand back yet, so keep listening.
            }
        }

        public void PushBack(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty) return;
            _pending.InsertRange(0, bytes.ToArray());
        }

        // Collapses "\r\n" into a single "\r" in place, compacting the buffer, so neither reader
        // has to reason about split line endings on its own. Returns the new, possibly smaller,
        // byte count.
        private int FilterCrLf(byte[] buffer, int offset, int count)
        {
            int write = offset;
            for (int read = offset; read < offset + count; read++)
            {
                byte b = buffer[read];

                if (_swallowNextLf)
                {
                    _swallowNextLf = false;
                    if (b == 10) continue;
                }

                if (b == 13) _swallowNextLf = true;

                buffer[write++] = b;
            }

            return write - offset;
        }
    }
}
