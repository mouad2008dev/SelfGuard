using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DomainListBuilder;

class Program
{
    // ── Whitelist: general platforms that appear in sites.json via specific paths only ──
    private static readonly HashSet<string> Whitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "google.com","bing.com","yahoo.com","microsoft.com","apple.com",
        "mozilla.org","opera.com","videolan.org","reddit.com","twitter.com",
        "pinterest.com","imgur.com","mediafire.com","rapidshare.com",
        "tumblr.com","cnet.com","softonic.com","bleepingcomputer.com"
    };

    // ── AES-256 key derivation constants (must match SelfGuardService/DomainList.cs) ──
    private const string EncPassword = "SG_B10cK3r_K3y_AES256_2024!";
    private static readonly byte[] EncSalt =
    {
        0x53,0x47,0x42,0x6C,0x6F,0x63,0x6B,0x65,
        0x72,0x53,0x61,0x6C,0x74,0x32,0x30,0x32
    };

    static int Main(string[] args)
    {
        string inputPath  = args.Length > 0 ? args[0] : "sites.json";
        string outputPath = args.Length > 1 ? args[1]
            : Path.Combine("..", "SelfGuardService", "Resources", "blocked_domains.enc");

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"[ERROR] File not found: {inputPath}");
            return 1;
        }

        Console.WriteLine($"[*] Reading {inputPath} …");
        string json = File.ReadAllText(inputPath, Encoding.UTF8);

        using var doc  = JsonDocument.Parse(json);
        var root       = doc.RootElement;
        var domains    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int total = 0, skipped = 0;

        foreach (var entry in root.EnumerateArray())
        {
            total++;
            string? raw     = ExtractRaw(entry);
            string? cleaned = Normalize(raw);

            if (cleaned is null)             { skipped++; continue; }
            if (Whitelist.Contains(cleaned)) { skipped++; continue; }

            domains.Add(cleaned);
        }

        var sorted = domains.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        Console.WriteLine($"[+] Total entries : {total}");
        Console.WriteLine($"[+] Skipped       : {skipped}");
        Console.WriteLine($"[+] Unique blocked : {sorted.Count}");

        byte[] plainBytes  = Encoding.UTF8.GetBytes(string.Join('\n', sorted));
        byte[] cipherBytes = Encrypt(plainBytes);

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        File.WriteAllBytes(outputPath, cipherBytes);
        Console.WriteLine($"[+] Saved encrypted list → {Path.GetFullPath(outputPath)} ({cipherBytes.Length:N0} bytes)");

        Console.WriteLine("\n[*] First 20 blocked domains:");
        foreach (var d in sorted.Take(20)) Console.WriteLine($"    {d}");
        return 0;
    }

    // ── Domain extraction: parent_domain > domain > hostname ──
    static string? ExtractRaw(JsonElement e)
    {
        foreach (string field in new[] { "parent_domain", "domain", "hostname" })
        {
            if (e.TryGetProperty(field, out var v) &&
                v.ValueKind != JsonValueKind.Null &&
                !string.IsNullOrWhiteSpace(v.GetString()))
                return v.GetString();
        }
        return null;
    }

    // ── Normalization: strip protocol/path/port/www/querystring, lowercase ──
    static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Strip query string
        int q = raw.IndexOf('?');
        if (q >= 0) raw = raw[..q];

        // Strip protocol
        foreach (string pfx in new[] { "https://", "http://" })
            if (raw.StartsWith(pfx, StringComparison.OrdinalIgnoreCase))
            { raw = raw[pfx.Length..]; break; }

        // Strip path
        int slash = raw.IndexOf('/');
        if (slash >= 0) raw = raw[..slash];

        // Strip port (only after last dot group)
        int lastDot = raw.LastIndexOf('.');
        int colon   = raw.IndexOf(':', lastDot >= 0 ? lastDot : 0);
        if (colon >= 0) raw = raw[..colon];

        raw = raw.Trim().ToLowerInvariant();

        // Strip leading www.
        if (raw.StartsWith("www.", StringComparison.Ordinal))
            raw = raw[4..];

        // Basic validity: must contain a dot, length >= 4
        if (!raw.Contains('.') || raw.Length < 4) return null;

        return raw;
    }

    // ── AES-256-CBC encryption with PBKDF2-SHA256 key derivation ──
    static byte[] Encrypt(byte[] plaintext)
    {
        using var kdf = new Rfc2898DeriveBytes(EncPassword, EncSalt, 100_000, HashAlgorithmName.SHA256);
        byte[] key = kdf.GetBytes(32);
        byte[] iv  = kdf.GetBytes(16);

        using var aes = Aes.Create();
        aes.Key     = key;
        aes.IV      = iv;
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cs.Write(plaintext, 0, plaintext.Length);
            cs.FlushFinalBlock();
        }
        return ms.ToArray();
    }
}
