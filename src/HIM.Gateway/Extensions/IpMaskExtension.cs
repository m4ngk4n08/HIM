using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace HIM.Gateway.Extensions
{
    /// <summary>
    /// Task 23C: the one place that decides what a "masked IP" looks like, so every render path
    /// (today, just /defense's ban table) calls the same helper instead of each hand-rolling its
    /// own octet-splitting - which is how a leak gets in later.
    ///
    /// This is a *different* boundary from SanitizerExtension.RedactPhone, the same way the
    /// gateway already keeps SanitizeLogInput (control-character stripping) separate from it:
    /// masking hides which specific host an address belongs to, redaction hides free text that
    /// looks like a phone number. Neither substitutes for the other.
    /// </summary>
    public static class IpMaskExtension
    {
        /// <summary>
        /// IPv4: keeps the network, masks the host - "203.0.113.9" -> "203.0.113.x".
        /// IPv6: keeps the first 48 bits (3 hextets - a common ISP allocation boundary) and masks
        /// everything after it - "2001:db8::1" -> "2001:db8:0::x". Anything that isn't a parsable
        /// IP address (null, empty, malformed) renders as "invalid" rather than echoing the raw
        /// input back onto the screen.
        /// </summary>
        public static string MaskIp(string? ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out var parsed))
                return "invalid";

            var bytes = parsed.GetAddressBytes();

            if (parsed.AddressFamily == AddressFamily.InterNetwork)
            {
                return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.x";
            }

            if (parsed.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var hextets = Enumerable.Range(0, 3)
                    .Select(i => ((bytes[i * 2] << 8) | bytes[i * 2 + 1]).ToString("x"));
                return string.Join(":", hextets) + "::x";
            }

            return "invalid";
        }
    }
}
