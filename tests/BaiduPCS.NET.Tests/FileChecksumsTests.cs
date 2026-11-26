using System.Security.Cryptography;
using BaiduPCS.Transfers;

namespace BaiduPCS.Tests;

public sealed class FileChecksumsTests
{
    [Fact]
    public async Task ComputesTheStandardCrc32AndMd5Vectors()
    {
        using var stream = new MemoryStream("123456789"u8.ToArray());
        FileChecksums checksum = await FileChecksums.ComputeAsync(stream, TestContext.Current.CancellationToken);

        Assert.Equal(9, checksum.Length);
        Assert.Equal(0xcbf43926u, checksum.Crc32);
        Assert.Equal("25f9e794323b453885f5181f1b624d0b", checksum.Md5);
        Assert.Equal(checksum.Md5, checksum.SliceMd5);
    }

    [Fact]
    public async Task LimitsSliceHashToTheFirst256KiB()
    {
        byte[] contents = new byte[FileChecksums.SliceLength + 1];
        contents[^1] = 255;
        using var stream = new MemoryStream(contents);

        FileChecksums checksum = await FileChecksums.ComputeAsync(stream, TestContext.Current.CancellationToken);

        Assert.Equal(Convert.ToHexStringLower(MD5.HashData(contents.AsSpan(0, FileChecksums.SliceLength))), checksum.SliceMd5);
        Assert.NotEqual(checksum.Md5, checksum.SliceMd5);
    }
}
