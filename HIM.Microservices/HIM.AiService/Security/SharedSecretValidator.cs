using System.Security.Cryptography;
using System.Text;

namespace HIM.AiService.Security
{
    public static class SharedSecretValidator
    {
        public static bool IsValid(string? provided, string expectedSecret)
        {
            if (provided is null) return false;

            var expectedBytes = Encoding.UTF8.GetBytes(expectedSecret);
            var providedBytes = Encoding.UTF8.GetBytes(provided);

            if (providedBytes.Length != expectedBytes.Length) return false;

            return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
        }
    }
}
