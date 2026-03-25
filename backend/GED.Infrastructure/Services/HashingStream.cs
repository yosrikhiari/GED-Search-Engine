using System.Security.Cryptography;

namespace GED.Infrastructure.Services;

/// <summary>
/// A stream wrapper that computes a hash while reading/writing data.
/// Writes to the underlying stream and simultaneously computes a hash.
/// </summary>
public class HashingStream : Stream
{
    private readonly Stream _innerStream;
    private readonly HashAlgorithm _hashAlgorithm;
    private readonly bool _leaveInnerStreamOpen;

    public HashingStream(Stream innerStream, HashAlgorithm hashAlgorithm, bool leaveInnerStreamOpen = false)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _hashAlgorithm = hashAlgorithm ?? throw new ArgumentNullException(nameof(hashAlgorithm));
        _leaveInnerStreamOpen = leaveInnerStreamOpen;
    }

    public byte[] Hash => _hashAlgorithm.Hash ?? Array.Empty<byte>();

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanWrite => _innerStream.CanWrite;
    public override long Length => _innerStream.Length;
    public override long Position
    {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }

    public override void Flush()
    {
        _innerStream.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesRead = _innerStream.Read(buffer, offset, count);
        if (bytesRead > 0)
        {
            _hashAlgorithm.TransformBlock(buffer, offset, bytesRead, buffer, offset);
        }
        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _innerStream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        _innerStream.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _innerStream.Write(buffer, offset, count);
        _hashAlgorithm.TransformBlock(buffer, offset, count, buffer, offset);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Finalize the hash - must call after all writes are done
            _hashAlgorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            if (!_leaveInnerStreamOpen)
            {
                _innerStream.Dispose();
            }
            _hashAlgorithm.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Extension methods for computing file hashes while streaming.
/// </summary>
public static class StreamExtensions
{
    private const int BufferSize = 81920; // 80KB buffer for efficient streaming

    /// <summary>
    /// Computes SHA-256 hash while copying source stream to destination stream.
    /// Uses chunked copying to minimize memory usage.
    /// </summary>
    public static async Task<string> ComputeSha256WhileCopyingAsync(
        this Stream source,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        using var sha256 = SHA256.Create();
        var buffer = new byte[BufferSize];
        int bytesRead;
        
        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            sha256.TransformBlock(buffer, 0, bytesRead, buffer, 0);
        }
        
        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        
        return Convert.ToHexString(sha256.Hash!).ToLower();
    }

    /// <summary>
    /// Writes stream to file while computing SHA-256 hash in a single pass.
    /// Memory efficient - only uses an 80KB buffer.
    /// </summary>
    public static async Task<(string FilePath, string Hash)> WriteToFileWithHashAsync(
        this Stream source,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var filePath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(filePath);
        
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var fileStream = File.Create(filePath);
        var hash = await ComputeSha256WhileCopyingAsync(source, fileStream, cancellationToken);
        
        return (filePath, hash);
    }
}
