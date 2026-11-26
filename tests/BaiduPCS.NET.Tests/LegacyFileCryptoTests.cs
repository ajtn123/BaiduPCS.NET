using BaiduPCS.Utilities;

namespace BaiduPCS.Tests;

public sealed class LegacyFileCryptoTests
{
    private const string Plaintext = "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e51";
    private const string Key = "2b7e151628aed2a6abf7158809cf4f3c";

    [Theory]
    [InlineData("aes-128-ctr", "f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff", "874d6191b620e3261bef6864990db6ce9806f66b7970fdff8617187bb9fffdff")]
    [InlineData("aes-128-cfb", "000102030405060708090a0b0c0d0e0f", "3b3fd92eb72dad20333449f8e83cfb4ac8a64537a0b3a93fcde3cdad9f1ce58b")]
    [InlineData("aes-128-ofb", "000102030405060708090a0b0c0d0e0f", "3b3fd92eb72dad20333449f8e83cfb4a7789508d16918f03f53c52dac54ed825")]
    public async Task DecryptsIvPrefixedNistVectors(string method, string iv, string ciphertext)
    {
        using var encrypted = new MemoryStream(Convert.FromHexString(iv + ciphertext));
        using var result = new MemoryStream();

        await LegacyFileCrypto.DecryptAsync(encrypted, result, method, Convert.FromHexString(Key), gzip: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(Convert.FromHexString(Plaintext), result.ToArray());
    }

    [Theory]
    [InlineData("aes-128-ctr")]
    [InlineData("aes-128-cfb")]
    [InlineData("aes-128-ofb")]
    [InlineData("aes-192-ctr")]
    [InlineData("aes-192-cfb")]
    [InlineData("aes-192-ofb")]
    [InlineData("aes-256-ctr")]
    [InlineData("aes-256-cfb")]
    [InlineData("aes-256-ofb")]
    public async Task RoundTripsGzipAndZeroPaddedKeys(string method)
    {
        byte[] source = new byte[2049];
        new Random(222).NextBytes(source);
        using var input = new MemoryStream(source);
        using var encrypted = new MemoryStream();
        using var output = new MemoryStream();
        byte[] shortKey = "k"u8.ToArray();
        byte[] paddedKey = new byte[32];
        shortKey.CopyTo(paddedKey, 0);

        await LegacyFileCrypto.EncryptAsync(input, encrypted, method, shortKey, cancellationToken: TestContext.Current.CancellationToken);
        encrypted.Position = 0;
        await LegacyFileCrypto.DecryptAsync(encrypted, output, method, paddedKey, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(source, output.ToArray());
    }

    [Fact]
    public async Task RoundTripsAnEmptyGzipMember()
    {
        using var empty = new MemoryStream();
        using var encrypted = new MemoryStream();
        using var output = new MemoryStream();
        byte[] key = "fixture"u8.ToArray();

        await LegacyFileCrypto.EncryptAsync(empty, encrypted, "aes-128-ctr", key, cancellationToken: TestContext.Current.CancellationToken);
        encrypted.Position = 0;
        await LegacyFileCrypto.DecryptAsync(encrypted, output, "aes-128-ctr", key, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task RejectsAGzipStreamWithoutItsTrailer()
    {
        using var input = new MemoryStream(Convert.FromHexString(Plaintext));
        using var encrypted = new MemoryStream();
        using var output = new MemoryStream();
        byte[] key = Convert.FromHexString(Key);

        await LegacyFileCrypto.EncryptAsync(input, encrypted, "aes-128-ctr", key, cancellationToken: TestContext.Current.CancellationToken);
        encrypted.SetLength(encrypted.Length - 8);
        encrypted.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => LegacyFileCrypto.DecryptAsync(encrypted, output, "aes-128-ctr", key,
            cancellationToken: TestContext.Current.CancellationToken));
    }
}
