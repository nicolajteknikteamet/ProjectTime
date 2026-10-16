using System.Security.Cryptography;
using System.Text;

namespace ProjectTime.Server.Services;

public static class ApiKeys
{
    /// <summary>Skapar en ny slumpmässig nyckel. Bara hashen sparas i databasen.</summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "pt_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Hash(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
