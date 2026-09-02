using System.Text.RegularExpressions;

namespace HIM.AiService.Extensions
{
    public static class SanitizerExtension
    {
        private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
        RegexOptions.Compiled);

        private static readonly Regex PhoneRegex = new(
            @"(\+?\d{1,3}[- ]?)?\(?\d{3}\)?[- ]?\d{3}[- ]?\d{4}",
            RegexOptions.Compiled);

        public static string Redact(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = EmailRegex.Replace(input, "[REDACTED_EMAIL]");
            result = PhoneRegex.Replace(result, "[REDACTED_PHONE]");
            return result;
        }

        // SEC-02: phone-only redaction for the knowledge-base ingestion boundary. Redact() above
        // also strips email, which is right for logs but wrong here - the contact email is the
        // deliberate public channel and must stay retrievable by RAG.
        public static string RedactPhone(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return PhoneRegex.Replace(input, "[REDACTED_PHONE]");
        }
    }
}
