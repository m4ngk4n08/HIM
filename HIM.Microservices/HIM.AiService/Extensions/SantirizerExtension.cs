using System.Text.RegularExpressions;

namespace HIM.AiService.Extensions
{
    public static class SantirizerExtension
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
    }
}
