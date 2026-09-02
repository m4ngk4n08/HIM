using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace HIM.Gateway.Extensions
{
    /// <summary>
    /// SEC-02: the last boundary before the visitor. CommandService.Redact()'d safeQuestion and
    /// safeResponse only ever fed the logger - console.Write(panel) rendered the unredacted
    /// response straight to the visitor. This wraps the AI token stream instead, so redaction
    /// happens on the text that actually reaches the console.
    ///
    /// The response arrives as a sequence of arbitrarily-sized chunks off an HTTP stream, not
    /// aligned to word or pattern boundaries, so a naive per-chunk regex would miss a phone
    /// number split across two chunks. This holds back a tail long enough to contain the longest
    /// match, and never emits a redacted prefix that would cut through a match still forming at
    /// the boundary - see SafeCutPoint.
    /// </summary>
    public static class EgressFilterExtension
    {
        // Comfortably above the longest string SanitizerExtension.PhoneRegex can match (~20
        // chars for a fully-formatted "+1 (555) 123-4567"-shaped number).
        private const int HoldBackLength = 32;

        public static async IAsyncEnumerable<string> RedactPiiAsync(
            this IAsyncEnumerable<string> source,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var buffer = new StringBuilder();

            await foreach (var chunk in source.WithCancellation(ct))
            {
                if (string.IsNullOrEmpty(chunk)) continue;
                buffer.Append(chunk);

                if (buffer.Length <= HoldBackLength) continue;

                var raw = buffer.ToString();
                var cut = SafeCutPoint(raw, raw.Length - HoldBackLength);

                if (cut <= 0) continue;

                var toEmit = SanitizerExtension.RedactPhone(raw[..cut]);
                buffer.Clear();
                buffer.Append(raw[cut..]);

                if (toEmit.Length > 0) yield return toEmit;
            }

            if (buffer.Length > 0)
                yield return SanitizerExtension.RedactPhone(buffer.ToString());
        }

        /// <summary>
        /// Never lets the cut fall inside a match: if the phone pattern matches something that
        /// straddles <paramref name="tentativeCut"/>, the cut moves back to the start of that
        /// match instead, so the whole match stays buffered - and therefore redactable as one
        /// piece - regardless of where the underlying network chunk boundaries fell.
        /// </summary>
        private static int SafeCutPoint(string raw, int tentativeCut)
        {
            var cut = tentativeCut;
            foreach (Match m in SanitizerExtension.PhoneRegex.Matches(raw))
            {
                if (m.Index < cut && m.Index + m.Length > cut)
                    cut = m.Index;
            }
            return cut;
        }
    }
}
