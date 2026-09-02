using System.Security.Cryptography;
using System.Text;

namespace AI.DocumentAssistant.Server.Understanding;

public static class DocumentUnderstandingContentHasher
{
    public static string Compute(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }
}
