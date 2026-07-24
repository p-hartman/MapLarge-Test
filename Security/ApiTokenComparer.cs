using System.Security.Cryptography;
using System.Text;

namespace TestProject.Security;

internal static class ApiTokenComparer
{
    public static bool Equals(string presented, string expected)
    {
        // hash first so FixedTimeEquals always receives equal-length buffers
        var ha = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        var hb = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(ha, hb);
    }
}
