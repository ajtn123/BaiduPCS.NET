using BaiduPCS.Configuration;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal abstract class ProfileCommand<TSettings> : AsyncCommand<TSettings> where TSettings : CommonSettings
{
    protected ProfileStore Store { get; } = new(ProfileStore.DefaultDirectory());
}
