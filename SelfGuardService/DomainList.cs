using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SelfGuardService;

/// <summary>Loads and decrypts the embedded AES-256 blocked-domain list.</summary>
public sealed class DomainList
{
    private const string EncPassword = "SG_B10cK3r_K3y_AES256_2024!";
    private static readonly byte[] EncSalt =
    {
        0x53,0x47,0x42,0x6C,0x6F,0x63,0x6B,0x65,
        0x72,0x53,0x61,0x6C,0x74,0x32,0x30,0x32
    };

    private readonly HashSet<string> _domains = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _domains.Count;

    public DomainList()
    {
        byte[] cipher = ReadEmbedded("blocked_domains.enc");
        string plain  = Decrypt(cipher);
        foreach (var line in plain.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _domains.Add(line);
    }

    /// <summary>True if host equals, or is a sub-domain of, any blocked domain (*.domain.com).</summary>
    public bool IsBlocked(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];

        if (_domains.Contains(host)) return true;

        int idx = host.IndexOf('.');
        while (idx >= 0)
        {
            string parent = host[(idx + 1)..];
            if (_domains.Contains(parent)) return true;
            idx = host.IndexOf('.', idx + 1);
        }
        return false;
    }

    private static byte[] ReadEmbedded(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string Decrypt(byte[] cipher)
    {
        using var kdf = new Rfc2898DeriveBytes(EncPassword, EncSalt, 100_000, HashAlgorithmName.SHA256);
        byte[] key = kdf.GetBytes(32);
        byte[] iv  = kdf.GetBytes(16);

        using var aes = Aes.Create();
        aes.Key = key; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;

        using var ms = new MemoryStream(cipher);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);
        return sr.ReadToEnd();
    }
}
