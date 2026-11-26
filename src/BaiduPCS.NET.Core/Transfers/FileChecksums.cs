// Adapts the BaiduPCS-Go checksum encoding to .NET. See THIRD-PARTY-NOTICES.md.
using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;

namespace BaiduPCS.Transfers;

public sealed record FileChecksums(long Length, string Md5, string SliceMd5, uint Crc32)
{
    public const int SliceLength = 256 * 1024;
    private static readonly uint[] Crc32Table = CreateTable();

    public string FormatLink(string fileName) => string.Join('#', Md5, SliceMd5, Length.ToString(CultureInfo.InvariantCulture), fileName);

    public static async Task<FileChecksums> ComputeAsync(Stream source, CancellationToken cancellationToken = default)
    {
        using IncrementalHash md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using IncrementalHash slice = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        long length = 0;
        uint crc = uint.MaxValue;
        try
        {
            int count;
            while ((count = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
            {
                md5.AppendData(buffer, 0, count);
                if (length < SliceLength)
                    slice.AppendData(buffer, 0, (int)Math.Min(SliceLength - length, count));

                crc = AppendCrc32(crc, buffer.AsSpan(0, count));
                length += count;
            }

            return new(length, Convert.ToHexStringLower(md5.GetHashAndReset()),
                Convert.ToHexStringLower(slice.GetHashAndReset()), ~crc);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    internal static uint AppendCrc32(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
            crc = Crc32Table[(crc ^ value) & 255] ^ (crc >> 8);

        return crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint remainder = index;
            for (int bit = 0; bit < 8; bit++)
                remainder = (remainder >> 1) ^ ((remainder & 1) == 0 ? 0 : 0xedb88320);

            table[index] = remainder;
        }

        return table;
    }
}
