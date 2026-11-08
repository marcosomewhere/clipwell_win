using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClipwellWin.Services;

/// <summary>
/// The random data key behind all encrypted databases and files. Each database and the file format get their own
/// key derived from it (HKDF), so one key never serves two ciphers.
/// </summary>
public sealed class DataKey
{
    public const int KeyBytes = 32;
    public const int IdBytes = 8;
    private readonly byte[] _key;
    private byte[]? _fileKey;

    internal DataKey(byte[] key, byte[] recoveryWrap, string? recoveryKey)
    {
        if (key.Length != KeyBytes) throw new ArgumentException("Ungültige Schlüssellänge.", nameof(key));
        _key = key;
        Id = ComputeId(key);
        RecoveryWrap = recoveryWrap;
        RecoveryKey = recoveryKey;
    }

    /// <summary>Public fingerprint written into every encrypted file, so a file names the key it needs.</summary>
    public byte[] Id { get; }

    /// <summary>The data key encrypted with the recovery key; stored next to the key and in every encrypted file.</summary>
    public byte[] RecoveryWrap { get; }

    /// <summary>Formatted recovery key, known on the account that created or recovered the key.</summary>
    public string? RecoveryKey { get; }

    internal byte[] FileKey => _fileKey ??= Derive("files");

    internal byte[] Material => _key;

    /// <summary>Passphrase for one SQLite3 Multiple Ciphers database; hex only, so it needs no quoting.</summary>
    public string DatabasePassword(string name) => Convert.ToHexStringLower(Derive("db:" + name));

    private byte[] Derive(string purpose)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32, info: Encoding.UTF8.GetBytes("Merkautomat." + purpose));

    public static byte[] ComputeId(byte[] key)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, key, IdBytes, info: "Merkautomat.key-id"u8.ToArray());

    /// <summary>New random data key plus a new random recovery key.</summary>
    public static DataKey Create()
    {
        var key = RandomNumberGenerator.GetBytes(KeyBytes);
        var recovery = RecoveryCode.Generate();
        return new DataKey(key, RecoveryCode.Wrap(key, recovery), RecoveryCode.Format(recovery));
    }
}

/// <summary>
/// Recovery key: 160 random bits as 32 Crockford-Base32 characters in groups of four. It encrypts the data key
/// (PBKDF2-SHA256 plus AES-256-GCM), so data and backups open on another account or PC.
/// </summary>
public static class RecoveryCode
{
    public const int Bytes = 20;
    private const int Iterations = 210_000;
    private const byte WrapVersion = 1;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly byte[] Aad = "Merkautomat.recovery.v1"u8.ToArray();

    public static byte[] Generate() => RandomNumberGenerator.GetBytes(Bytes);

    public static string Format(byte[] code)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in code)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        var raw = sb.ToString();
        return string.Join("-", Enumerable.Range(0, raw.Length / 4).Select(i => raw.Substring(i * 4, 4)));
    }

    /// <summary>Reads a typed recovery key; spaces, dashes, lower case and the look-alikes O/I/L are tolerated.</summary>
    public static byte[]? Parse(string? text)
    {
        if (text == null) return null;
        var chars = text.ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '-')
            .Select(c => c switch { 'O' => '0', 'I' or 'L' => '1', _ => c }).ToArray();
        if (chars.Length != Bytes * 8 / 5) return null;
        var result = new byte[Bytes];
        int buffer = 0, bits = 0, at = 0;
        foreach (var c in chars)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0) return null;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                result[at++] = (byte)(buffer >> (bits - 8));
                bits -= 8;
            }
        }
        return result;
    }

    public static byte[] Wrap(byte[] dataKey, byte[] code)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var kek = Rfc2898DeriveBytes.Pbkdf2(code, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[dataKey.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(kek, 16)) aes.Encrypt(nonce, dataKey, cipher, tag, Aad);
        CryptographicOperations.ZeroMemory(kek);
        return [WrapVersion, .. salt, .. nonce, .. cipher, .. tag];
    }

    /// <summary>The data key, or null when the recovery key does not fit this wrap.</summary>
    public static byte[]? Unwrap(byte[] wrap, byte[] code)
    {
        if (wrap.Length != 1 + 16 + 12 + DataKey.KeyBytes + 16 || wrap[0] != WrapVersion) return null;
        var salt = wrap.AsSpan(1, 16);
        var nonce = wrap.AsSpan(17, 12);
        var cipher = wrap.AsSpan(29, DataKey.KeyBytes);
        var tag = wrap.AsSpan(29 + DataKey.KeyBytes, 16);
        var kek = Rfc2898DeriveBytes.Pbkdf2(code, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var key = new byte[DataKey.KeyBytes];
        try
        {
            using var aes = new AesGcm(kek, 16);
            aes.Decrypt(nonce, cipher, tag, key, Aad);
            return key;
        }
        catch (AuthenticationTagMismatchException) { return null; }
        finally { CryptographicOperations.ZeroMemory(kek); }
    }
}

public enum DataKeyState
{
    /// <summary>Key loaded for this Windows account.</summary>
    Ready,
    /// <summary>No data existed; a new key was created and its recovery key still has to be shown.</summary>
    Created,
    /// <summary>Encrypted data exists, but this account cannot open the key (other PC, new profile, reset password).</summary>
    NeedsRecovery,
    /// <summary>Encrypted data exists, but neither the key file nor any recovery information was found.</summary>
    Missing,
}

public sealed record DataKeyOpenResult(DataKeyState State, byte[]? RecoveryWrap = null, bool RecoveryKeyConfirmed = true);

/// <summary>
/// Loads and stores the data key in datakey.json: DPAPI (current user) holds the key and the recovery key, the
/// recovery wrap is stored in the clear. A new key is only ever created when no encrypted data exists, so a lost or
/// unreadable key never leads to data being overwritten.
/// </summary>
public static class DataProtection
{
    public const string KeyFileName = "datakey.json";
    private static readonly byte[] Entropy = "Merkautomat.DataKey.v1"u8.ToArray();
    private static readonly object Gate = new();
    private static DataKey? _current;
    private static readonly List<DataKey> Foreign = [];

    public static DataKey Current
    {
        get { lock (Gate) return _current ?? throw new InvalidOperationException("Der Datenschlüssel ist nicht geladen."); }
    }

    public static bool IsReady { get { lock (Gate) return _current != null; } }

    /// <summary>Makes <paramref name="key"/> the key for new data; a previous key stays known for reading.</summary>
    public static void Use(DataKey key)
    {
        lock (Gate)
        {
            if (_current != null && !_current.Id.AsSpan().SequenceEqual(key.Id)) AddForeign(_current);
            _current = key;
        }
    }

    /// <summary>Key of another installation, opened with its recovery key for an import in this session.</summary>
    public static void AddForeign(DataKey key) { lock (Gate) if (!Foreign.Any(k => k.Id.AsSpan().SequenceEqual(key.Id))) Foreign.Add(key); }

    public static DataKey? Find(ReadOnlySpan<byte> id)
    {
        lock (Gate)
        {
            if (_current != null && id.SequenceEqual(_current.Id)) return _current;
            foreach (var key in Foreign) if (id.SequenceEqual(key.Id)) return key;
            return null;
        }
    }

    public static string KeyFile(string dataDir) => Path.Combine(dataDir, KeyFileName);

    private sealed class StoredKey
    {
        public int Version { get; set; } = 1;
        public string KeyId { get; set; } = "";
        public string Protected { get; set; } = "";
        public string Recovery { get; set; } = "";
        public bool RecoveryKeyConfirmed { get; set; }
    }

    public static DataKeyOpenResult Open(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        var path = KeyFile(dataDir);
        StoredKey? stored = null;
        byte[]? wrap = null;
        if (File.Exists(path))
        {
            try
            {
                stored = JsonSerializer.Deserialize<StoredKey>(File.ReadAllText(path));
                if (stored != null) wrap = Convert.FromBase64String(stored.Recovery);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                // Kept for manual recovery; the recovery key still works through the wrap inside the data files.
                File.Move(path, path + $".defekt-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
                stored = null;
            }
        }
        if (stored != null && wrap != null)
        {
            try
            {
                var plain = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(stored.Protected), Entropy,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                var key = plain[..DataKey.KeyBytes];
                var recovery = Encoding.UTF8.GetString(plain, DataKey.KeyBytes, plain.Length - DataKey.KeyBytes);
                CryptographicOperations.ZeroMemory(plain);
                var dataKey = new DataKey(key, wrap, recovery.Length > 0 ? recovery : null);
                if (!string.Equals(Convert.ToHexString(dataKey.Id), stored.KeyId, StringComparison.OrdinalIgnoreCase))
                    throw new CryptographicException("Die Schlüsseldatei passt nicht zu ihrem Schlüssel.");
                Use(dataKey);
                return new(DataKeyState.Ready, wrap, stored.RecoveryKeyConfirmed);
            }
            catch (CryptographicException)
            {
                return new(DataKeyState.NeedsRecovery, wrap);
            }
        }

        if (EncryptedDataExists(dataDir))
        {
            var found = SecureFile.FindRecoveryWrap(EncryptedFileCandidates(dataDir));
            return found != null ? new(DataKeyState.NeedsRecovery, found) : new(DataKeyState.Missing);
        }

        var created = DataKey.Create();
        Save(dataDir, created, confirmed: false);
        Use(created);
        return new(DataKeyState.Created, created.RecoveryWrap, RecoveryKeyConfirmed: false);
    }

    /// <summary>Opens the data key with the recovery key and binds it to this Windows account.</summary>
    public static bool TryRecover(string dataDir, byte[] wrap, string input)
    {
        var key = TryUnwrap(wrap, input);
        if (key == null) return false;
        Save(dataDir, key, confirmed: true);
        Use(key);
        return true;
    }

    public static DataKey? TryUnwrap(byte[] wrap, string input)
    {
        var code = RecoveryCode.Parse(input);
        if (code == null) return null;
        var material = RecoveryCode.Unwrap(wrap, code);
        return material == null ? null : new DataKey(material, wrap, RecoveryCode.Format(code));
    }

    public static void ConfirmRecoveryKey(string dataDir)
    {
        var path = KeyFile(dataDir);
        var stored = JsonSerializer.Deserialize<StoredKey>(File.ReadAllText(path))!;
        stored.RecoveryKeyConfirmed = true;
        WriteAtomic(path, stored);
    }

    private static void Save(string dataDir, DataKey key, bool confirmed)
    {
        var recovery = Encoding.UTF8.GetBytes(key.RecoveryKey ?? "");
        byte[] plain = [.. key.Material, .. recovery];
        var protectedBytes = System.Security.Cryptography.ProtectedData.Protect(plain, Entropy,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(plain);
        WriteAtomic(KeyFile(dataDir), new StoredKey
        {
            KeyId = Convert.ToHexString(key.Id),
            Protected = Convert.ToBase64String(protectedBytes),
            Recovery = Convert.ToBase64String(key.RecoveryWrap),
            RecoveryKeyConfirmed = confirmed,
        });
    }

    private static void WriteAtomic(string path, StoredKey stored)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Encrypted databases or files are present (the key must then never be replaced by a new one).</summary>
    public static bool EncryptedDataExists(string dataDir)
        => EncryptedDatabase.Names.Any(n => EncryptedDatabase.IsEncryptedFile(Path.Combine(dataDir, n)))
           || EncryptedFileCandidates(dataDir).Any(SecureFile.IsEncryptedFile);

    /// <summary>Files that are stored encrypted, existing or not yet migrated.</summary>
    public static IEnumerable<string> EncryptedFileCandidates(string dataDir)
    {
        foreach (var name in new[] { "settings.json", "tasks.json", "background-discovery.json" })
            yield return Path.Combine(dataDir, name);
        foreach (var dir in ContentDirectories(dataDir))
        {
            if (!Directory.Exists(dir)) continue;
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files) yield return file;
        }
        if (Directory.Exists(dataDir))
            foreach (var file in Directory.GetFiles(dataDir, "tasks.json.defekt-*")) yield return file;
    }

    public static IEnumerable<string> ContentDirectories(string dataDir)
    {
        yield return Path.Combine(dataDir, AppPaths.NotesFolderName);
        yield return Path.Combine(dataDir, "kb", AppPaths.NotesFolderName);
        yield return Path.Combine(dataDir, AppPaths.RecordingsFolderName);
    }
}
