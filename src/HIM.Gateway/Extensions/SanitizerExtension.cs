using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace HIM.Gateway.Extensions
{
    public static class SanitizerExtension
    {
        private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
        RegexOptions.Compiled);

        // Internal so EgressFilterExtension can locate match boundaries (without redacting) to
        // avoid cutting a streamed response mid-pattern - see EgressFilterExtension.RedactPiiAsync.
        internal static readonly Regex PhoneRegex = new(
            @"(\+?\d{1,3}[- ]?)?\(?\d{3}\)?[- ]?\d{3}[- ]?\d{4}",
            RegexOptions.Compiled);

        public static string Redact(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = EmailRegex.Replace(input, "[REDACTED_EMAIL]");
            result = PhoneRegex.Replace(result, "[REDACTED_PHONE]");
            return result;
        }

        // SEC-02: phone-only redaction for the egress boundary (both the AI token stream and
        // directly-rendered portfolio text). Redact() above also strips email, which is wrong
        // here - the contact email is the deliberate public channel and must reach the visitor.
        public static string RedactPhone(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return PhoneRegex.Replace(input, "[REDACTED_PHONE]");
        }
    }
}
