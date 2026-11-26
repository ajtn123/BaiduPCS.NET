using System.Reflection;
using BaiduPCS.Netdisk;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli;

internal static class Program
{
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
        var app = new CommandApp();
        var completions = new ShellCompletions();
        app.Configure(config =>
        {
            config.SetApplicationName("baidupcs");
            config.SetApplicationVersion(typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0]);
            config.SetExceptionHandler((error, _) =>
            {
                if (error is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    return 130;

                if (error is CommandParseException)
                {
                    Console.Error.WriteLine("Invalid command or arguments. Use --help for usage.");
                    return 2;
                }

                if (error is ArgumentException or FormatException or OverflowException)
                {
                    Console.Error.WriteLine(error is FormatException or OverflowException
                        ? "Invalid value or number out of range."
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
                branch.SetDescription("Display and change client settings.");
                completions.Register<ShowConfigCommand>(branch, "config", "show").WithDescription("Display the current settings.");
                completions.Register<SetConfigCommand>(branch, "config", "set").WithDescription("Set one or more key=value options.");
                completions.Register<ResetConfigCommand>(branch, "config", "reset").WithDescription("Restore the default client settings.");
            });
            config.AddBranch("account", branch =>
            {
                branch.SetDescription("Manage Baidu accounts.");
                completions.Register<LoginCommand>(branch, "account", "login").WithDescription("Sign in using a password or an existing Baidu session.");
                completions.Register<ImportAccountCommand>(branch, "account", "import-go").WithDescription("Import a BaiduPCS-Go profile.");
                completions.Register<ListAccountsCommand>(branch, "account", "list").WithDescription("List saved accounts.");
                completions.Register<CurrentAccountCommand>(branch, "account", "current").WithDescription("Show the active account.");
                completions.Register<SwitchAccountCommand>(branch, "account", "switch").WithDescription("Switch the active account.");
                completions.Register<LogoutCommand>(branch, "account", "logout").WithDescription("Remove the active saved account.");
                completions.Register<SetAccessTokenCommand>(branch, "account", "set-access-token").WithDescription("Update the active account's access token.");
            });
            completions.Register<EnvironmentCommand>(config, "env").WithDescription("Show configuration paths and environment variables.");
            completions.Register<PrintWorkingDirectoryCommand>(config, "pwd").WithDescription("Print the active account's remote working directory.");
            completions.Register<QuotaCommand>(config, "quota").WithDescription("Show total, used, and free storage space.");
            completions.Register<ListFilesCommand>(config, "list").WithDescription("List a remote directory.");
            completions.Register<ChangeDirectoryCommand>(config, "cd").WithDescription("Set the active account's remote working directory.");
            completions.Register<SearchFilesCommand>(config, "search").WithDescription("Find files by name.");
            completions.Register<MetadataCommand>(config, "metadata").WithDescription("Show metadata for remote paths.");
            completions.Register<MatchCommand>(config, "match").WithDescription("Expand a remote wildcard pattern.");
            completions.Register<TreeCommand>(config, "tree").WithDescription("Show a remote directory tree.");
            completions.Register<CreateDirectoryCommand>(config, "mkdir").WithDescription("Create remote directories.");
            completions.Register<RemoveFilesCommand>(config, "remove").WithDescription("Remove files or directories from the drive.");
            completions.Register<CopyFilesCommand>(config, "copy").WithDescription("Copy files or directories on the drive.");
            completions.Register<MoveFilesCommand>(config, "move").WithDescription("Move files or directories on the drive.");
            completions.Register<RenameFileCommand>(config, "rename").WithDescription("Rename a file or directory within its parent.");
            completions.Register<DownloadLinksCommand>(config, "download-links").WithDescription("Resolve direct or Pan web download links.");
            completions.Register<DownloadCommand>(config, "download").WithDescription("Download files and directory trees from Baidu Netdisk.");
            completions.Register<UploadCommand>(config, "upload").WithDescription("Upload local files or directory trees to a remote directory.");
            completions.Register<ChecksumCommand>(config, "checksum").WithDescription("Calculate Baidu file checksums and checksum links.");
            completions.Register<ExportCommand>(config, "export").WithDescription("Export remote file checksums and directory structure.");
            completions.Register<ShellCommand>(config, "shell").WithData(completions).WithDescription("Start the interactive shell.");
            completions.Register<RunProcessCommand>(config, "run").WithDescription("Run a local executable with 'run -- EXECUTABLE ARG...'.");
            completions.Register<ClearCommand>(config, "clear").WithDescription("Clear the terminal.");
            completions.Register<ExitCommand>(config, "exit").WithDescription("Exit the BaiduPCS.NET shell.");
            config.AddBranch("offline", branch =>
            {
                branch.SetDescription("Manage server-side download tasks.");
                completions.Register<AddOfflineCommand>(branch, "offline", "add").WithDescription("Submit URLs for Baidu to download.");
                completions.Register<QueryOfflineCommand>(branch, "offline", "query").WithDescription("Show specific offline tasks.");
                completions.Register<ListOfflineCommand>(branch, "offline", "list").WithDescription("List offline tasks.");
                completions.Register<CancelOfflineCommand>(branch, "offline", "cancel").WithDescription("Cancel running offline tasks.");
                completions.Register<DeleteOfflineCommand>(branch, "offline", "delete").WithDescription("Delete offline task records.");
            });
            config.AddBranch("recycle", branch =>
            {
                branch.SetDescription("Manage the drive's recycle bin.");
                completions.Register<ListRecycleCommand>(branch, "recycle", "list").WithDescription("List deleted files.");
                completions.Register<RestoreRecycleCommand>(branch, "recycle", "restore").WithDescription("Restore files from the recycle bin.");
                completions.Register<DeleteRecycleCommand>(branch, "recycle", "delete").WithDescription("Permanently delete recycle-bin files.");
            });
            config.AddBranch("share", branch =>
            {
                branch.SetDescription("Create, manage, and import Baidu shares.");
                completions.Register<CreateShareCommand>(branch, "share", "create").WithDescription("Create a protected share for remote paths.");
                completions.Register<ListSharesCommand>(branch, "share", "list").WithDescription("List the active account's shares.");
                completions.Register<CancelShareCommand>(branch, "share", "cancel").WithDescription("Cancel shares by ID.");
                completions.Register<ImportShareCommand>(branch, "share", "import").WithDescription("Save shared files to the current remote directory.");
            });
            config.AddBranch("tool", branch =>
            {
                branch.SetDescription("Run local utilities.");
                completions.Register<BeijingTimeCommand>(branch, "tool", "time").WithDescription("Show the current time in Beijing.");
                completions.Register<IpCommand>(branch, "tool", "ip").WithDescription("Show local and public IP addresses.");
                completions.Register<EncryptCommand>(branch, "tool", "encrypt").WithDescription("Encrypt local files in the legacy format.");
                completions.Register<DecryptCommand>(branch, "tool", "decrypt").WithDescription("Decrypt legacy encrypted files.");
            });
        });
        return app.RunAsync(args, cancellationToken);
    }
}
