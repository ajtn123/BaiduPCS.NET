using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;
using Spectre.Console;
using Profile = BaiduPCS.Configuration.Profile;

namespace BaiduPCS.Cli.Commands;

internal abstract class RemoteCommand<TSettings> : ProfileCommand<TSettings> where TSettings : CommonSettings
{
    protected async Task<(Account Account, NetdiskClient Client)> OpenAsync(TSettings settings, CancellationToken token)
    {
        var (profile, client) = await OpenProfileAsync(settings, token);
        return (profile.RequireAccount(), client);
    }

    protected async Task<(Profile Profile, NetdiskClient Client)> OpenProfileAsync(TSettings settings, CancellationToken token)
    {
        Profile profile = await Store.ReadAsync(token);
        Account account = profile.RequireAccount();
        if (settings.Verbose || Environment.GetEnvironmentVariable(ProfileStore.VerboseVariable) == "1")
            Console.Error.WriteLine($"Baidu API operation: {GetType().Name}");

        return (profile, new NetdiskClient(profile.Settings, account));
    }

    protected static async Task<string[]> ExpandAsync(NetdiskClient client, Account account, IEnumerable<string> patterns,
        CancellationToken token)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (string pattern in patterns)
        {
            IReadOnlyList<string> matches = await client.MatchAsync(account.WorkingDirectory, pattern, token);
            if (matches.Count == 0)
                throw new InvalidOperationException($"The remote pattern '{pattern}' matched no paths.");

            paths.UnionWith(matches);
        }

        return [.. paths];
    }

    protected static async Task<int> ExecuteEachAsync<T>(CommonSettings settings, IEnumerable<T> items, Func<T, string> describe,
        Func<T, Task> action, CancellationToken token)
    {
        var display = new OperationDisplay(settings, "Item");
        int exitCode = 0;
        var queue = items.Select(item =>
        {
            string name = describe(item);
            return (Item: item, Name: name, Row: display.Enqueue(name));
        }).ToArray();
        await display.RunAsync(async () =>
        {
            foreach (var (item, name, queued) in queue)
            {
                token.ThrowIfCancellationRequested();
                OperationDisplay.Item row = display.Begin(queued);
                try
                {
                    await action(item);
                    display.End(row, "Done", plain: name);
                }
                catch (Exception error) when (error is BaiduApiException or HttpRequestException or InvalidDataException
                    || error is OperationCanceledException && !token.IsCancellationRequested)
                {
                    string failure = error is HttpRequestException ? "Baidu request failed." : error.Message;
                    display.End(row, failure, failed: true, plain: $"{name}: {failure}");
                    exitCode = 1;
                }
            }
        });

        return exitCode;
    }
}
