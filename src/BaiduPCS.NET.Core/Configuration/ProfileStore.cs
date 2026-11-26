using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BaiduPCS.Configuration;

public sealed class ProfileStore(string directory)
{
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<UploadConflict>(JsonNamingPolicy.KebabCaseLower) },
    };

    public const string DirectoryVariable = "BAIDUPCS_CONFIG_DIR";
    public const string VerboseVariable = "BAIDUPCS_VERBOSE";
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    private string FilePath => Path.Combine(DirectoryPath, "profile.json");

    public static string DefaultDirectory()
    {
        string? configured = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string parent = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : OperatingSystem.IsMacOS()
                ? Path.Combine(home, "Library", "Application Support")
                : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg && Path.IsPathFullyQualified(xdg)
                    ? xdg
                    : Path.Combine(home, ".config");
        return Path.Combine(parent, "BaiduPCS.NET");
    }

    public async Task<Profile> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            Profile? profile = await JsonSerializer.DeserializeAsync<Profile>(stream, Json, cancellationToken);
            if (profile is null || profile.Version != 1 || profile.Settings is null || profile.Accounts is null
                || profile.Accounts.Any(account => account is null || account.Id == 0 || account.Name is null
                    || account.Credentials is null || !RemotePath.IsAbsolute(account.WorkingDirectory))
                || profile.Accounts.Select(account => account.Id).Distinct().Count() != profile.Accounts.Count
                || (profile.ActiveAccountId.HasValue && profile.ActiveAccount is null))
            {
                throw new InvalidDataException("The BaiduPCS.NET profile has an unsupported version or invalid account data.");
            }

            return profile;
        }
        catch (FileNotFoundException)
        {
            return new();
        }
        catch (DirectoryNotFoundException)
        {
            return new();
        }
        catch (JsonException)
        {
            throw new InvalidDataException("The BaiduPCS.NET profile contains invalid JSON.");
        }
    }

    public async Task UpdateAsync(Action<Profile> update, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(DirectoryPath);
        else
            Directory.CreateDirectory(DirectoryPath, PrivateDirectory);

        await using FileStream updateLock = await LockAsync(cancellationToken);
        Profile profile = await ReadAsync(cancellationToken);
        update(profile);

        string temporaryPath = Path.Combine(DirectoryPath, $"profile-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, PrivateOptions(FileMode.CreateNew, FileShare.None)))
            {
                await JsonSerializer.SerializeAsync(stream, profile, Json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private async Task<FileStream> LockAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(DirectoryPath, "profile.lock");
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, PrivateOptions(FileMode.OpenOrCreate, FileShare.None));
            }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }

    private static FileStreamOptions PrivateOptions(FileMode mode, FileShare share)
    {
        var options = new FileStreamOptions
        {
            Mode = mode,
            Access = FileAccess.ReadWrite,
            Share = share,
            Options = FileOptions.Asynchronous,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = PrivateFile;

        return options;
    }
}
