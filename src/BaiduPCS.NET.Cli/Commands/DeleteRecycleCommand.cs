using BaiduPCS.Netdisk;

namespace BaiduPCS.Cli.Commands;

internal sealed class DeleteRecycleCommand : DeleteByIdCommand
{
    protected override string ClearPrompt => "Permanently delete everything in the recycle bin?";
    protected override Task<long> ClearAsync(NetdiskClient client, CancellationToken token) => client.ClearRecycleBinAsync(token);
    protected override Task DeleteAsync(NetdiskClient client, long id, CancellationToken token) => client.DeleteRecycleEntryAsync(id, token);
}
