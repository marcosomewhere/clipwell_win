using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Merkautomat.Services;

/// <summary>An encrypted file was written with a key this installation does not have; its recovery key opens it.</summary>
public sealed class ForeignKeyException(byte[] recoveryWrap)
    : CryptographicException("Die Datei wurde mit einem anderen Schlüssel verschlüsselt.")
{
    public byte[] RecoveryWrap { get; } = recoveryWrap;
}

/// <summary>
/// Encrypted file format for notes, recordings, tasks, settings and backups.
/// Header: "MKE1", version, key id (8 bytes), length and recovery wrap of the data key. Then records of at most
/// 64 KB plain text: length, nonce, AES-256-GCM cipher text, tag. Header and record number are authenticated, so
/// records cannot be swapped or moved between files. Appending adds records, which keeps a running recording
/// encrypted; a record cut off by a crash is dropped when reading. Files without the header are read as plain text
/// (data from before the encryption) and encrypted on their next write or by <see cref="DataMigration"/>.
/// </summary>
public static class SecureFile
{
    public const int RecordPlainMax = 64 * 1024;
    private const byte Version = 1;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private static readonly byte[] Magic = "MKE1"u8.ToArray();
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static bool IsEncrypted(ReadOnlySpan<byte> data) => data.Length >= Magic.Length && data[..Magic.Length].SequenceEqual(Magic);

    public static bool IsEncryptedFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var fs = OpenShared(path);
            Span<byte> head = stackalloc byte[4];
            return fs.ReadAtLeast(head, 4, throwOnEndOfStream: false) == 4 && IsEncrypted(head);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public static string ReadAllText(string path) => DecodeText(Decrypt(ReadAllBytesShared(path)));

    public static void WriteAllText(string path, string text)
    {
        using var stream = Create(path);
        stream.Write(Utf8NoBom.GetBytes(text));
    }

    /// <summary>Writes the text encrypted to a temporary file and moves it over <paramref name="path"/>.</summary>
    public static void WriteAllTextAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }

    public static byte[] Encrypt(ReadOnlySpan<byte> plain, DataKey? key = null)
    {
        using var output = new MemoryStream();
        using (var stream = new SecureWriteStream(output, key ?? DataProtection.Current, 0, writeHeader: true, leaveOpen: true))
            stream.Write(plain);
        return output.ToArray();
    }

    /// <summary>Plain bytes of <paramref name="data"/>; data without the header is returned unchanged.</summary>
    public static byte[] Decrypt(byte[] data)
    {
        if (!IsEncrypted(data)) return data;
        var header = ParseHeader(data);
        var key = DataProtection.Find(header.KeyId) ?? throw new ForeignKeyException(header.RecoveryWrap);
        using var aes = new AesGcm(key.FileKey, TagBytes);
        using var output = new MemoryStream();
        var pos = header.Length;
        for (var index = 0; ; index++)
        {
            if (!TryRecord(data, pos, out var cipherLength)) break;
            var nonce = data.AsSpan(pos + 4, NonceBytes);
            var cipher = data.AsSpan(pos + 4 + NonceBytes, cipherLength);
            var tag = data.AsSpan(pos + 4 + NonceBytes + cipherLength, TagBytes);
            var plain = new byte[cipherLength];
            try { aes.Decrypt(nonce, cipher, tag, plain, RecordAad(data.AsSpan(0, AadHeaderBytes), index)); }
            catch (AuthenticationTagMismatchException ex)
            {
                throw new CryptographicException("Die verschlüsselte Datei ist beschädigt oder wurde verändert.", ex);
            }
            output.Write(plain);
            pos += 4 + NonceBytes + cipherLength + TagBytes;
        }
        return output.ToArray();
    }

    /// <summary>New encrypted file; everything written goes out as records on Flush/Dispose or every 64 KB.</summary>
    public static Stream Create(string path, FileShare share = FileShare.Read)
    {
        var fs = new FileStream(path, FileMode.Create, FileAccess.Write, share);
        return new SecureWriteStream(fs, DataProtection.Current, 0, writeHeader: true, leaveOpen: false);
    }

    /// <summary>
    /// Appends records to an encrypted file (the recorder keeps it open while the knowledge base reads it). A plain
    /// file is encrypted first; a record cut off by a crash is removed so new records stay readable.
    /// </summary>
    public static Stream OpenAppend(string path)
    {
        if (File.Exists(path) && new FileInfo(path).Length > 0 && !IsEncryptedFile(path))
            WriteBytesEncryptedKeepingTime(path, File.ReadAllBytes(path));
        var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            if (fs.Length == 0) return new SecureWriteStream(fs, DataProtection.Current, 0, writeHeader: true, leaveOpen: false);
            var data = new byte[fs.Length];
            fs.ReadExactly(data);
            var header = ParseHeader(data);
            if (!header.KeyId.AsSpan().SequenceEqual(DataProtection.Current.Id))
                throw new ForeignKeyException(header.RecoveryWrap);
            var pos = header.Length;
            var count = 0;
            while (TryRecord(data, pos, out var cipherLength))
            {
                pos += 4 + NonceBytes + cipherLength + TagBytes;
                count++;
            }
            fs.SetLength(pos);
            fs.Position = pos;
            return new SecureWriteStream(fs, DataProtection.Current, count, writeHeader: false, leaveOpen: false);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    /// <summary>Recovery wrap from the first encrypted file found, for opening the key when datakey.json is lost.</summary>
    public static byte[]? FindRecoveryWrap(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            try
            {
                if (!IsEncryptedFile(file)) continue;
                using var fs = OpenShared(file);
                var head = new byte[Math.Min(fs.Length, 256)];
                fs.ReadExactly(head);
                return ParseHeader(head).RecoveryWrap;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }
        return null;
    }

    /// <summary>Recovery wrap of one encrypted file, e.g. a backup from another PC.</summary>
    public static byte[]? RecoveryWrapOf(byte[] data) => IsEncrypted(data) ? ParseHeader(data).RecoveryWrap : null;

    /// <summary>
    /// Encrypts a plain file in place: written to a temporary file first, then the plain original is overwritten
    /// with zeros and replaced. The write time stays, because notes and the search index rely on it.
    /// </summary>
    public static bool EncryptInPlace(string path)
    {
        if (!File.Exists(path) || IsEncryptedFile(path)) return false;
        WriteBytesEncryptedKeepingTime(path, File.ReadAllBytes(path));
        return true;
    }

    public const string EncryptingSuffix = ".encrypting";
    public const string PlainOldSuffix = ".plain-old";

    private static void WriteBytesEncryptedKeepingTime(string path, byte[] plain)
    {
        var created = File.GetCreationTimeUtc(path);
        var written = File.GetLastWriteTimeUtc(path);
        var tmp = path + EncryptingSuffix;
        var old = path + PlainOldSuffix;
        File.WriteAllBytes(tmp, Encrypt(plain));
        CryptographicOperations.ZeroMemory(plain);
        File.SetCreationTimeUtc(tmp, created);
        File.SetLastWriteTimeUtc(tmp, written);
        // At every moment one complete copy exists under a known name; ResumeInterrupted finishes a crashed run.
        File.Move(path, old, overwrite: true);
        File.Move(tmp, path);
        File.SetLastWriteTimeUtc(path, written);
        Shred(old);
    }

    /// <summary>
    /// Finishes an encryption interrupted by a crash: an encrypted copy without its original takes its place, a
    /// remaining plain original (already replaced) is overwritten and removed, an incomplete copy is dropped.
    /// </summary>
    public static void ResumeInterrupted(string leftover)
    {
        if (leftover.EndsWith(PlainOldSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var target = leftover[..^PlainOldSuffix.Length];
            if (File.Exists(target)) Shred(leftover);
            else File.Move(leftover, target);
            return;
        }
        if (!leftover.EndsWith(EncryptingSuffix, StringComparison.OrdinalIgnoreCase)) return;
        var original = leftover[..^EncryptingSuffix.Length];
        if (!File.Exists(original) && IsCompleteEncryptedFile(leftover)) File.Move(leftover, original);
        else Shred(leftover);
    }

    private static bool IsCompleteEncryptedFile(string path)
    {
        try
        {
            Decrypt(ReadAllBytesShared(path));
            return IsEncryptedFile(path);
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidDataException or IOException) { return false; }
    }

    /// <summary>
    /// Overwrites a file with zeros before it goes. On SSDs the controller may still keep old blocks for a while;
    /// full-disk encryption (BitLocker) covers those.
    /// </summary>
    public static void Shred(string path, bool deleteAfter = true)
    {
        if (!File.Exists(path)) return;
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                var zeros = new byte[64 * 1024];
                for (long left = fs.Length; left > 0; left -= zeros.Length)
                    fs.Write(zeros, 0, (int)Math.Min(zeros.Length, left));
                fs.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogCrash(ex); }
        if (deleteAfter) File.Delete(path);
    }

    public static string DecodeText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static byte[] ReadAllBytesShared(string path)
    {
        using var fs = OpenShared(path);
        using var output = new MemoryStream();
        fs.CopyTo(output);
        return output.ToArray();
    }

    private static FileStream OpenShared(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    // ---- Format ------------------------------------------------------------------------------------------------

    private const int AadHeaderBytes = 4 + 1 + DataKey.IdBytes;

    private sealed record Header(byte[] KeyId, byte[] RecoveryWrap, int Length);

    private static Header ParseHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < AadHeaderBytes + 1 || !IsEncrypted(data) || data[4] != Version)
            throw new InvalidDataException("Unbekanntes Format der verschlüsselten Datei.");
        var wrapLength = data[AadHeaderBytes];
        var length = AadHeaderBytes + 1 + wrapLength;
        if (data.Length < length) throw new InvalidDataException("Die verschlüsselte Datei ist unvollständig.");
        return new(data.Slice(5, DataKey.IdBytes).ToArray(), data.Slice(AadHeaderBytes + 1, wrapLength).ToArray(), length);
    }

    internal static byte[] HeaderFor(DataKey key)
    {
        if (key.RecoveryWrap.Length > byte.MaxValue) throw new InvalidOperationException("Recovery wrap too long.");
        return [.. Magic, Version, .. key.Id, (byte)key.RecoveryWrap.Length, .. key.RecoveryWrap];
    }

    internal static byte[] RecordAad(ReadOnlySpan<byte> headerStart, int index)
    {
        var aad = new byte[AadHeaderBytes + 4];
        headerStart[..AadHeaderBytes].CopyTo(aad);
        BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(AadHeaderBytes), index);
        return aad;
    }

    private static bool TryRecord(byte[] data, int pos, out int cipherLength)
    {
        cipherLength = 0;
        if (data.Length - pos < 4) return false;
        cipherLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
        return cipherLength is >= 0 and <= RecordPlainMax && (long)pos + 4 + NonceBytes + cipherLength + TagBytes <= data.Length;
    }

    internal static void WriteRecord(Stream output, AesGcm aes, byte[] headerStart, int index, ReadOnlySpan<byte> plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagBytes];
        aes.Encrypt(nonce, plain, cipher, tag, RecordAad(headerStart, index));
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, plain.Length);
        output.Write(length);
        output.Write(nonce);
        output.Write(cipher);
        output.Write(tag);
    }
}

/// <summary>Write-only stream that turns everything written into encrypted records (see <see cref="SecureFile"/>).</summary>
internal sealed class SecureWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly AesGcm _aes;
    private readonly byte[] _headerStart;
    private readonly bool _leaveOpen;
    private readonly MemoryStream _buffer = new();
    private int _index;
    private bool _disposed;

    public SecureWriteStream(Stream inner, DataKey key, int nextIndex, bool writeHeader, bool leaveOpen)
    {
        _inner = inner;
        _leaveOpen = leaveOpen;
        _aes = new AesGcm(key.FileKey, 16);
        _index = nextIndex;
        var header = SecureFile.HeaderFor(key);
        _headerStart = header[..(4 + 1 + DataKey.IdBytes)];
        if (writeHeader)
        {
            _inner.Write(header);
            _inner.Flush();
        }
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (buffer.Length > 0)
        {
            var take = (int)Math.Min(buffer.Length, SecureFile.RecordPlainMax - _buffer.Length);
            _buffer.Write(buffer[..take]);
            buffer = buffer[take..];
            if (_buffer.Length >= SecureFile.RecordPlainMax) EmitRecord();
        }
    }

    public override void Flush()
    {
        if (_disposed) return;
        if (_buffer.Length > 0) EmitRecord();
        _inner.Flush();
    }

    private void EmitRecord()
    {
        SecureFile.WriteRecord(_inner, _aes, _headerStart, _index++, _buffer.GetBuffer().AsSpan(0, (int)_buffer.Length));
        CryptographicOperations.ZeroMemory(_buffer.GetBuffer());
        _buffer.SetLength(0);
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            Flush();
            _disposed = true;
            _aes.Dispose();
            if (!_leaveOpen) _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
