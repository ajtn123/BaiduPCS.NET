using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using BaiduPCS.Transfers;

namespace BaiduPCS.Utilities;

public static class LegacyFileCrypto
{
    public static async Task EncryptAsync(Stream input, Stream output, string method, ReadOnlyMemory<byte> key,
        bool gzip = true, CancellationToken cancellationToken = default)
    {
        (int keySize, Mode mode) = ParseMethod(method);
        byte[] iv = RandomNumberGenerator.GetBytes(StreamTransform.BlockSize);
        await output.WriteAsync(iv, cancellationToken);
        using var transform = new StreamTransform(key.Span, keySize, iv, mode, decrypt: false);
        await using var cipher = new CryptoStream(output, transform, CryptoStreamMode.Write, leaveOpen: true);
        if (gzip)
        {
            byte[] first = new byte[1];
            if (await input.ReadAsync(first, cancellationToken) == 0)
            {
                // GZipStream omits the member when no bytes were written.
                await cipher.WriteAsync(Convert.FromHexString("1f8b080000000000000303000000000000000000"), cancellationToken);
            }
            else
            {
                await using var compressed = new GZipStream(cipher, CompressionLevel.Optimal, leaveOpen: true);
                await compressed.WriteAsync(first, cancellationToken);
                await input.CopyToAsync(compressed, cancellationToken);
            }
        }
        else
            await input.CopyToAsync(cipher, cancellationToken);

        await cipher.FlushFinalBlockAsync(cancellationToken);
    }

    public static async Task DecryptAsync(Stream input, Stream output, string method, ReadOnlyMemory<byte> key,
        bool gzip = true, CancellationToken cancellationToken = default)
    {
        (int keySize, Mode mode) = ParseMethod(method);
        byte[] iv = new byte[StreamTransform.BlockSize];
        await input.ReadExactlyAsync(iv, cancellationToken);
        if (gzip && input.CanSeek && input.Length - input.Position < 20)
            throw new InvalidDataException("The encrypted gzip stream is incomplete.");

        using var transform = new StreamTransform(key.Span, keySize, iv, mode, decrypt: true);
        await using var cipher = new CryptoStream(input, transform, CryptoStreamMode.Read, leaveOpen: true);
        if (gzip)
            await ReadGzipAsync(cipher, output, cancellationToken);
        else
            await cipher.CopyToAsync(output, cancellationToken);
    }

    private static async Task ReadGzipAsync(Stream cipher, Stream output, CancellationToken token)
    {
        using var source = new GzipTrailerStream(cipher);
        await using var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: true);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        uint crc = uint.MaxValue;
        uint size = 0;
        try
        {
            int count;
            while ((count = await gzip.ReadAsync(buffer, token)) != 0)
            {
                crc = FileChecksums.AppendCrc32(crc, buffer.AsSpan(0, count));
                size = unchecked(size + (uint)count);
                await output.WriteAsync(buffer.AsMemory(0, count), token);
            }

            await source.CopyToAsync(Stream.Null, token);
            if (!source.MatchesFooter(~crc, size))
                throw new InvalidDataException("The encrypted gzip stream has an invalid or missing trailer.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    public static bool Supports(string method) => TryParseMethod(method, out _, out _);

    private static (int Size, Mode Mode) ParseMethod(string method) => TryParseMethod(method, out int size, out Mode mode)
        ? (size, mode) : throw new ArgumentException("Use aes-128, aes-192, or aes-256 with ctr, cfb, or ofb.");

    private static bool TryParseMethod(string method, out int size, out Mode mode)
    {
        string[] parts = method.ToLowerInvariant().Split('-');
        size = 0;
        mode = default;
        if (parts is not ["aes", var bits, var suffix])
            return false;

        size = bits switch { "128" => 16, "192" => 24, "256" => 32, _ => 0 };
        mode = suffix switch { "ctr" => Mode.Ctr, "cfb" => Mode.Cfb, "ofb" => Mode.Ofb, _ => Mode.Unknown };
        return size != 0 && mode != Mode.Unknown;
    }

    private enum Mode { Unknown, Ctr, Cfb, Ofb }

    private sealed class GzipTrailerStream(Stream source) : Stream
    {
        private readonly byte[] _tail = new byte[8];
        private long _bytesRead;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            Remember(buffer.AsSpan(offset, read));
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            Remember(buffer.Span[..read]);
            return read;
        }

        internal bool MatchesFooter(uint crc, uint size) => _bytesRead >= 20
            && BinaryPrimitives.ReadUInt32LittleEndian(_tail) == crc
            && BinaryPrimitives.ReadUInt32LittleEndian(_tail.AsSpan(4)) == size;

        private void Remember(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length >= _tail.Length)
                bytes[^_tail.Length..].CopyTo(_tail);
            else if (!bytes.IsEmpty)
            {
                _tail.AsSpan(bytes.Length).CopyTo(_tail);
                bytes.CopyTo(_tail.AsSpan(_tail.Length - bytes.Length));
            }

            _bytesRead += bytes.Length;
        }
    }

    private sealed class StreamTransform : ICryptoTransform
    {
        internal const int BlockSize = 16;
        private readonly Aes _aes;
        private readonly ICryptoTransform _block;
        private readonly byte[] _feedback;
        private readonly byte[] _keystream = new byte[BlockSize];
        private readonly Mode _mode;
        private readonly bool _decrypt;
        private int _position = BlockSize;

        internal StreamTransform(ReadOnlySpan<byte> secret, int keySize, byte[] iv, Mode mode, bool decrypt)
        {
            byte[] key = new byte[keySize];
            secret[..Math.Min(secret.Length, keySize)].CopyTo(key);
            try
            {
                _aes = Aes.Create();
                _aes.Mode = CipherMode.ECB;
                _aes.Padding = PaddingMode.None;
                _aes.Key = key;
                _block = _aes.CreateEncryptor();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            _feedback = iv;
            _mode = mode;
            _decrypt = decrypt;
        }

        public int InputBlockSize => 1;
        public int OutputBlockSize => 1;
        public bool CanTransformMultipleBlocks => true;
        public bool CanReuseTransform => false;

        public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
        {
            ReadOnlySpan<byte> input = inputBuffer.AsSpan(inputOffset, inputCount);
            Span<byte> output = outputBuffer.AsSpan(outputOffset, inputCount);
            for (int index = 0; index < input.Length; index++)
            {
                if (_position == BlockSize)
                    Advance();

                byte original = input[index];
                byte transformed = (byte)(original ^ _keystream[_position]);
                if (_mode == Mode.Cfb)
                    _feedback[_position] = _decrypt ? original : transformed;

                output[index] = transformed;
                _position++;
            }

            return inputCount;
        }

        public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
        {
            var output = new byte[inputCount];
            TransformBlock(inputBuffer, inputOffset, inputCount, output, 0);
            return output;
        }

        private void Advance()
        {
            _block.TransformBlock(_feedback, 0, BlockSize, _keystream, 0);
            _position = 0;
            if (_mode == Mode.Ofb)
                _keystream.CopyTo(_feedback, 0);
            else if (_mode == Mode.Ctr)
            {
                for (int index = _feedback.Length - 1; index >= 0; index--)
                {
                    if (++_feedback[index] != 0)
                        break;
                }
            }
        }

        public void Dispose()
        {
            _block.Dispose();
            _aes.Dispose();
            CryptographicOperations.ZeroMemory(_feedback);
            CryptographicOperations.ZeroMemory(_keystream);
        }
    }
}
