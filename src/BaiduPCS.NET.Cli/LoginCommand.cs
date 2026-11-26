using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli;

internal sealed class LoginCommand : ProfileCommand<LoginCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandOption("--username <NAME>")] public string? Username { get; init; }
        [CommandOption("--password-stdin")] public bool PasswordStdin { get; init; }
        [CommandOption("--cookies <COOKIE-HEADER>")] public string? Cookies { get; init; }
        [CommandOption("--cookies-stdin")] public bool CookiesStdin { get; init; }
        [CommandOption("--bduss <TOKEN>")] public string? Bduss { get; init; }
        [CommandOption("--bduss-stdin")] public bool BdussStdin { get; init; }
        [CommandOption("--ptoken <TOKEN>")] public string? Ptoken { get; init; }
        [CommandOption("--stoken <TOKEN>")] public string? Stoken { get; init; }
        [CommandOption("--manual-uid <UID>")] public ulong? ManualUid { get; init; }

        internal bool IsCookieLogin => Cookies is not null || CookiesStdin;
        internal bool IsTokenLogin => Bduss is not null || BdussStdin;

        public override ValidationResult Validate() => (IsCookieLogin && IsTokenLogin || Cookies is not null && CookiesStdin
            || Bduss is not null && BdussStdin || (IsCookieLogin || IsTokenLogin) && (Username is not null || PasswordStdin))
            ? ValidationResult.Error("Choose one sign-in method and one source for its credentials.")
            : !IsTokenLogin && (Ptoken is not null || Stoken is not null)
                ? ValidationResult.Error("PTOKEN and STOKEN are options for BDUSS sign-in.")
            : ManualUid == 0 ? ValidationResult.Error("The manual account UID must be positive.") : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ClientSettings configuration = (await Store.ReadAsync(cancellationToken)).Settings;
        if (settings.ManualUid.HasValue && string.IsNullOrWhiteSpace(configuration.ManualAccountName))
            throw new ArgumentException("Configure manual-account-name before specifying a manual UID.");

        Credentials credentials;
        if (settings.IsCookieLogin)
        {
            string raw = settings.Cookies ?? await ReadStandardInputAsync(cancellationToken);
            credentials = new Credentials { Cookies = raw };
        }
        else if (settings.IsTokenLogin)
        {
            string bduss = settings.Bduss ?? await ReadStandardInputAsync(cancellationToken);
            credentials = new Credentials { Bduss = bduss, Ptoken = settings.Ptoken ?? "", Stoken = settings.Stoken ?? "" };
        }
        else
        {
            string username = settings.Username ?? Prompt("Baidu username:");
            string password = settings.PasswordStdin ? await ReadStandardInputAsync(cancellationToken, trim: false)
                : Prompt("Baidu password:", secret: true);
            using var passport = new PassportLogin(configuration);
            credentials = await passport.SignInAsync(username, password, new TerminalPrompts(), cancellationToken);
        }

        Account account = await AccountIdentity.ResolveAsync(configuration, credentials, settings.ManualUid, cancellationToken);
        await Store.UpdateAsync(profile =>
        {
            if (profile.Accounts.Find(saved => saved.Id == account.Id) is { } existing)
            {
                if (account.Id == 24 && settings.ManualUid is null && configuration.ManualAccountName.Length > 0
                    && !string.Equals(existing.Name, account.Name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The legacy fallback UID is already in use. Specify a different --manual-uid.");

                account.WorkingDirectory = existing.WorkingDirectory;
                account.Credentials.AccessToken = existing.Credentials.AccessToken;
            }

            profile.Activate(account);
        }, cancellationToken);
        Console.WriteLine($"Signed in as {account.Name} ({account.Id}).");
        return 0;
    }

    private static async Task<string> ReadStandardInputAsync(CancellationToken token, bool trim = true)
    {
        string? value = await Console.In.ReadLineAsync(token);
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Standard input did not contain a credential.");

        return trim ? value.Trim() : value;
    }

    private static string Prompt(string label, bool secret = false)
    {
        RequireInteractive();
        var prompt = new TextPrompt<string>(label);
        if (secret)
            prompt.Secret();
        return AnsiConsole.Prompt(prompt);
    }

    private static void RequireInteractive()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
            throw new InvalidOperationException("This sign-in step requires an interactive terminal. For unattended use, supply an existing session.");
    }

    private sealed class TerminalPrompts : IPassportPrompts
    {
        public async ValueTask<string> RequestImageCodeAsync(ReadOnlyMemory<byte> image, CancellationToken cancellationToken)
        {
            RequireInteractive();
            string path = Path.Combine(Path.GetTempPath(), $"baidupcs-challenge-{Guid.NewGuid():N}.png");
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                await using (var file = new FileStream(path, options))
                    await file.WriteAsync(image, cancellationToken);
                Console.Error.WriteLine($"Open the verification image at {path}");
                return Prompt("Image verification code:", secret: true);
            }
            finally
            {
                File.Delete(path);
            }
        }

        public ValueTask<VerificationChannel> ChooseChannelAsync(string phone, string email, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireInteractive();
            VerificationChannel[] options = phone.Length > 0 && email.Length == 0 ? [VerificationChannel.Mobile]
                : email.Length > 0 && phone.Length == 0 ? [VerificationChannel.Email]
                : [VerificationChannel.Mobile, VerificationChannel.Email];
            var prompt = new SelectionPrompt<VerificationChannel>().Title("Choose where Baidu should send the verification code.")
                .AddChoices(options).UseConverter(channel => channel == VerificationChannel.Mobile ? "Phone" : "Email");
            return ValueTask.FromResult(AnsiConsole.Prompt(prompt));
        }

        public ValueTask<string> RequestVerificationCodeAsync(VerificationChannel channel, bool retry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (retry)
                Console.Error.WriteLine("Baidu did not accept the previous verification code.");
            return ValueTask.FromResult(Prompt("Verification code:", secret: true));
        }
    }
}
