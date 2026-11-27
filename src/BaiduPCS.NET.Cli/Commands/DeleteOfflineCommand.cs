using BaiduPCS.Netdisk;

namespace BaiduPCS.Cli.Commands;

internal sealed class DeleteOfflineCommand : DeleteByIdCommand
{
    protected override string ClearPrompt => "Clear all offline task records?";
    protected override Task<long> ClearAsync(NetdiskClient client, CancellationToken token) => client.ClearOfflineTasksAsync(token);
    protected override Task DeleteAsync(NetdiskClient client, long id, CancellationToken token) => client.DeleteOfflineTaskAsync(id, token);
}
