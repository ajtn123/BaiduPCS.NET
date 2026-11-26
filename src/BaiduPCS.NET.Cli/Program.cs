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
        });
        return app.RunAsync(args, cancellationToken);
    }
}
