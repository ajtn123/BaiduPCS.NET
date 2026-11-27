using System.Reflection;
using BaiduPCS.Netdisk;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli;

internal static class Program
{
    internal static string Version { get; } = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    internal static bool RestartAfterUpdate { get; set; }

    private static async Task<int> Main(string[] args)
    {
        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            if (shutdown.IsCancellationRequested)
                return;

            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        Console.CancelKeyPress += cancel;
        try
        {
            if (args.Length > 0 && args[0] == ApplyUpdateCommand.HelperCommand)
                return await ApplyUpdateCommand.FinishWindowsUpdateAsync(args, shutdown.Token);

#if BAIDUPCS_STANDALONE
            if (OperatingSystem.IsWindows() && Environment.ProcessPath is { } executable)
                ApplyUpdateCommand.RemoveCompletedWindowsStages(Path.GetDirectoryName(executable)!);
#endif
            return await RunAsync(args.Length == 0
                ? [!Console.IsInputRedirected && !Console.IsOutputRedirected ? "shell" : "--help"] : args, shutdown.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    internal static Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length > 2 && args[0] is "upload" or "u")
        {
            int stdin = Array.IndexOf(args, "-", 1);
            if (stdin > 0 && args[stdin - 1] != "--as")
            {
                args = [.. args];
                args[stdin] = "--stdin";
            }
        }

        var app = new CommandApp();
        var completions = new ShellCompletions();
        app.Configure(config =>
        {
            config.SetApplicationName("baidupcs");
            config.SetApplicationVersion(Version);
            config.SetExceptionHandler((error, _) =>
            {
                if (error is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    return 130;

                if (error is CommandParseException)
                {
                    Console.Error.WriteLine(Ui.Text("Invalid command or arguments. Use --help for usage."));
                    return 2;
                }

                if (error is ArgumentException or FormatException or OverflowException)
                {
                    Console.Error.WriteLine(error is FormatException or OverflowException
                        ? Ui.Text("Invalid value or number out of range.")
                        : error.Message);
                    return 2;
                }

                Console.Error.WriteLine(error is InvalidOperationException or InvalidDataException or BaiduApiException
                    ? error.Message
                    : $"Operation failed ({error.GetType().Name}).");
                return 1;
            });

            config.AddBranch("config", branch =>
            {
                branch.SetDescription(Ui.Text("Display and change client settings."));
                completions.Register<ShowConfigCommand>(branch, "config", "show").WithDescription(Ui.Text("Display the current settings."));
                completions.Register<SetConfigCommand>(branch, "config", "set").WithDescription(Ui.Text("Set one or more key=value options."));
                completions.Register<ResetConfigCommand>(branch, "config", "reset").WithDescription(Ui.Text("Restore the default client settings."));
            });
            config.AddBranch("account", branch =>
            {
                branch.SetDescription(Ui.Text("Manage Baidu accounts."));
                completions.Register<LoginCommand>(branch, "account", "login").WithDescription(Ui.Text("Sign in using a password or an existing Baidu session."));
                completions.Register<ImportAccountCommand>(branch, "account", "import-go").WithDescription(Ui.Text("Import a BaiduPCS-Go profile."));
                completions.Register<ListAccountsCommand>(branch, "account", "list").WithDescription(Ui.Text("List saved accounts."));
                completions.Register<CurrentAccountCommand>(branch, "account", "current").WithDescription(Ui.Text("Show the active account."));
                completions.Register<SwitchAccountCommand>(branch, "account", "switch").WithDescription(Ui.Text("Switch the active account."));
                completions.Register<LogoutCommand>(branch, "account", "logout").WithDescription(Ui.Text("Remove the active saved account."));
                completions.Register<SetAccessTokenCommand>(branch, "account", "set-access-token").WithDescription(Ui.Text("Update the active account's access token."));
            });
            completions.Register<EnvironmentCommand>(config, "env").WithDescription(Ui.Text("Show configuration paths and environment variables."));
            completions.Register<PrintWorkingDirectoryCommand>(config, "pwd").WithDescription(Ui.Text("Print the active account's remote working directory."));
            completions.Register<QuotaCommand>(config, "quota").WithDescription(Ui.Text("Show total, used, and free storage space."));
            completions.Register<ListFilesCommand>(config, "list", "ls", "ll").WithDescription(Ui.Text("List a remote directory."));
            completions.Register<ChangeDirectoryCommand>(config, "cd").WithDescription(Ui.Text("Set the active account's remote working directory."));
            completions.Register<SearchFilesCommand>(config, "search").WithDescription(Ui.Text("Find files by name."));
            completions.Register<MetadataCommand>(config, "metadata", "meta").WithDescription(Ui.Text("Show metadata for remote paths."));
            completions.Register<MatchCommand>(config, "match").WithDescription(Ui.Text("Expand a remote wildcard pattern."));
            completions.Register<TreeCommand>(config, "tree").WithDescription(Ui.Text("Show a remote directory tree."));
            completions.Register<CreateDirectoryCommand>(config, "mkdir").WithDescription(Ui.Text("Create remote directories."));
            completions.Register<RemoveFilesCommand>(config, "remove", "rm").WithDescription(Ui.Text("Remove files or directories from the drive."));
            completions.Register<CopyFilesCommand>(config, "copy", "cp").WithDescription(Ui.Text("Copy files or directories on the drive."));
            completions.Register<MoveFilesCommand>(config, "move", "mv").WithDescription(Ui.Text("Move files or directories on the drive."));
            completions.Register<RenameFileCommand>(config, "rename").WithDescription(Ui.Text("Rename a file or directory within its parent."));
            completions.Register<DownloadLinksCommand>(config, "download-links", "locate").WithDescription(Ui.Text("Resolve direct or Pan web download links."));
            completions.Register<DownloadCommand>(config, "download", "d").WithDescription(Ui.Text("Download files and directory trees from Baidu Netdisk."));
            completions.Register<UploadCommand>(config, "upload", "u").WithDescription(Ui.Text("Upload local files or directory trees to a remote directory."));
            completions.Register<ChecksumCommand>(config, "checksum").WithDescription(Ui.Text("Calculate Baidu file checksums and checksum links."));
            completions.Register<ExportCommand>(config, "export").WithDescription(Ui.Text("Export remote file checksums and directory structure."));
            completions.Register<ShellCommand>(config, "shell").WithData(completions).WithDescription(Ui.Text("Start the interactive shell."));
            completions.Register<RunProcessCommand>(config, "run").WithDescription(Ui.Text("Run a local executable with 'run -- EXECUTABLE ARG...'."));
            completions.Register<ClearCommand>(config, "clear", "cls").WithDescription(Ui.Text("Clear the terminal."));
            completions.Register<ExitCommand>(config, "exit", "quit").WithDescription(Ui.Text("Exit the BaiduPCS.NET shell."));
            config.AddBranch("offline", branch =>
            {
                branch.SetDescription(Ui.Text("Manage server-side download tasks."));
                completions.Register<AddOfflineCommand>(branch, "offline", "add").WithDescription(Ui.Text("Submit URLs for Baidu to download."));
                completions.Register<QueryOfflineCommand>(branch, "offline", "query").WithDescription(Ui.Text("Show specific offline tasks."));
                completions.Register<ListOfflineCommand>(branch, "offline", "list").WithDescription(Ui.Text("List offline tasks."));
                completions.Register<CancelOfflineCommand>(branch, "offline", "cancel").WithDescription(Ui.Text("Cancel running offline tasks."));
                completions.Register<DeleteOfflineCommand>(branch, "offline", "delete").WithDescription(Ui.Text("Delete offline task records."));
            });
            config.AddBranch("recycle", branch =>
            {
                branch.SetDescription(Ui.Text("Manage the drive's recycle bin."));
                completions.Register<ListRecycleCommand>(branch, "recycle", "list").WithDescription(Ui.Text("List deleted files."));
                completions.Register<RestoreRecycleCommand>(branch, "recycle", "restore").WithDescription(Ui.Text("Restore files from the recycle bin."));
                completions.Register<DeleteRecycleCommand>(branch, "recycle", "delete").WithDescription(Ui.Text("Permanently delete recycle-bin files."));
            });
            config.AddBranch("share", branch =>
            {
                branch.SetDescription(Ui.Text("Create, manage, and import Baidu shares."));
                completions.Register<CreateShareCommand>(branch, "share", "create").WithDescription(Ui.Text("Create a protected share for remote paths."));
                completions.Register<ListSharesCommand>(branch, "share", "list").WithDescription(Ui.Text("List the active account's shares."));
                completions.Register<CancelShareCommand>(branch, "share", "cancel").WithDescription(Ui.Text("Cancel shares by ID."));
                completions.Register<ImportShareCommand>(branch, "share", "import").WithDescription(Ui.Text("Save shared files to the current remote directory."));
                completions.Register<InspectShareCommand>(branch, "share", "inspect").WithDescription(Ui.Text("List files in a Baidu share without importing them."));
            });
            config.AddBranch("tool", branch =>
            {
                branch.SetDescription(Ui.Text("Run local utilities."));
                completions.Register<BeijingTimeCommand>(branch, "tool", "time").WithDescription(Ui.Text("Show the current time in Beijing."));
                completions.Register<IpCommand>(branch, "tool", "ip").WithDescription(Ui.Text("Show local and public IP addresses."));
                completions.Register<EncryptCommand>(branch, "tool", "encrypt").WithDescription(Ui.Text("Encrypt local files in the legacy format."));
                completions.Register<DecryptCommand>(branch, "tool", "decrypt").WithDescription(Ui.Text("Decrypt legacy encrypted files."));
            });
            config.AddBranch("update", branch =>
            {
                branch.SetDescription(Ui.Text("Check for and install stable releases."));
                completions.Register<CheckUpdateCommand>(branch, "update", "check").WithDescription(Ui.Text("Check the latest stable release."));
                completions.Register<ApplyUpdateCommand>(branch, "update", "apply").WithDescription(Ui.Text("Install a newer stable release."));
            });
        });
        return app.RunAsync(args, cancellationToken);
    }
}
