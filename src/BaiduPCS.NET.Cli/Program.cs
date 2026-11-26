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
            return await RunAsync(args.Length == 0 ? ["--help"] : args, shutdown.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    internal static Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var app = new CommandApp();
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
                branch.AddCommand<ShowConfigCommand>("show").WithDescription("Display the current settings.");
                branch.AddCommand<SetConfigCommand>("set").WithDescription("Set one or more key=value options.");
                branch.AddCommand<ResetConfigCommand>("reset").WithDescription("Restore the default client settings.");
            });
            config.AddBranch("account", branch =>
            {
                branch.SetDescription("Manage Baidu accounts.");
                branch.AddCommand<ImportAccountCommand>("import-go").WithDescription("Import a BaiduPCS-Go profile.");
                branch.AddCommand<ListAccountsCommand>("list").WithDescription("List saved accounts.");
                branch.AddCommand<CurrentAccountCommand>("current").WithDescription("Show the active account.");
                branch.AddCommand<SwitchAccountCommand>("switch").WithDescription("Switch the active account.");
                branch.AddCommand<LogoutCommand>("logout").WithDescription("Remove the active saved account.");
                branch.AddCommand<SetAccessTokenCommand>("set-access-token").WithDescription("Update the active account's access token.");
            });
            config.AddCommand<EnvironmentCommand>("env").WithDescription("Show configuration paths and environment variables.");
            config.AddCommand<PrintWorkingDirectoryCommand>("pwd").WithDescription("Print the active account's remote working directory.");
            config.AddCommand<QuotaCommand>("quota").WithDescription("Show total, used, and free storage space.");
            config.AddCommand<ListFilesCommand>("list").WithDescription("List a remote directory.");
            config.AddCommand<ChangeDirectoryCommand>("cd").WithDescription("Set the active account's remote working directory.");
            config.AddCommand<SearchFilesCommand>("search").WithDescription("Find files by name.");
            config.AddCommand<MetadataCommand>("metadata").WithDescription("Show metadata for remote paths.");
            config.AddCommand<MatchCommand>("match").WithDescription("Expand a remote wildcard pattern.");
            config.AddCommand<TreeCommand>("tree").WithDescription("Show a remote directory tree.");
            config.AddCommand<CreateDirectoryCommand>("mkdir").WithDescription("Create remote directories.");
            config.AddCommand<RemoveFilesCommand>("remove").WithDescription("Remove files or directories from the drive.");
            config.AddCommand<CopyFilesCommand>("copy").WithDescription("Copy files or directories on the drive.");
            config.AddCommand<MoveFilesCommand>("move").WithDescription("Move files or directories on the drive.");
            config.AddCommand<RenameFileCommand>("rename").WithDescription("Rename a file or directory within its parent.");
            config.AddCommand<DownloadLinksCommand>("download-links").WithDescription("Resolve direct or Pan web download links.");
            config.AddCommand<DownloadCommand>("download").WithDescription("Download files and directory trees from Baidu Netdisk.");
            config.AddCommand<UploadCommand>("upload").WithDescription("Upload local files or directory trees to a remote directory.");
            config.AddCommand<ChecksumCommand>("checksum").WithDescription("Calculate Baidu file checksums and checksum links.");
            config.AddBranch("offline", branch =>
            {
                branch.SetDescription("Manage server-side download tasks.");
                branch.AddCommand<AddOfflineCommand>("add").WithDescription("Submit URLs for Baidu to download.");
                branch.AddCommand<QueryOfflineCommand>("query").WithDescription("Show specific offline tasks.");
                branch.AddCommand<ListOfflineCommand>("list").WithDescription("List offline tasks.");
                branch.AddCommand<CancelOfflineCommand>("cancel").WithDescription("Cancel running offline tasks.");
                branch.AddCommand<DeleteOfflineCommand>("delete").WithDescription("Delete offline task records.");
            });
            config.AddBranch("recycle", branch =>
            {
                branch.SetDescription("Manage the drive's recycle bin.");
                branch.AddCommand<ListRecycleCommand>("list").WithDescription("List deleted files.");
                branch.AddCommand<RestoreRecycleCommand>("restore").WithDescription("Restore files from the recycle bin.");
                branch.AddCommand<DeleteRecycleCommand>("delete").WithDescription("Permanently delete recycle-bin files.");
            });
            config.AddBranch("share", branch =>
            {
                branch.SetDescription("Create, manage, and import Baidu shares.");
                branch.AddCommand<CreateShareCommand>("create").WithDescription("Create a protected share for remote paths.");
                branch.AddCommand<ListSharesCommand>("list").WithDescription("List the active account's shares.");
                branch.AddCommand<CancelShareCommand>("cancel").WithDescription("Cancel shares by ID.");
                branch.AddCommand<ImportShareCommand>("import").WithDescription("Save shared files to the current remote directory.");
            });
        });
        return app.RunAsync(args, cancellationToken);
    }
}
