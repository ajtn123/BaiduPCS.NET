using BaiduPCS.Configuration;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class EnvironmentCommand : ProfileCommand<CommonSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Configuration directory: {Store.DirectoryPath}");
        Console.WriteLine($"{ProfileStore.DirectoryVariable}={Environment.GetEnvironmentVariable(ProfileStore.DirectoryVariable)}");
        Console.WriteLine($"{ProfileStore.VerboseVariable}={Environment.GetEnvironmentVariable(ProfileStore.VerboseVariable)}");
        Console.WriteLine($"{LegacyProfile.DirectoryVariable}={Environment.GetEnvironmentVariable(LegacyProfile.DirectoryVariable)}");
        return Task.FromResult(0);
    }
}
