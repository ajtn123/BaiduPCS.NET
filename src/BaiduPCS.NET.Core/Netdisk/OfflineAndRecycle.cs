using System.Globalization;
using System.Text.Json;

namespace BaiduPCS.Netdisk;

public sealed record OfflineFile(string Name, long Size);

public sealed record OfflineTask(long Id, long Status, string Name, string Destination, string Source, long Size,
    long CompletedBytes, DateTimeOffset Created, DateTimeOffset Started, DateTimeOffset Finished, long Type, long Result,
    IReadOnlyList<OfflineFile> Files);

public sealed record RecycleEntry(RemoteEntry Entry, long RemainingTime);

public sealed partial class NetdiskClient
{
    public async Task<long> AddOfflineTaskAsync(string source, string destination, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https" or "ftp" or "ed2k" or "magnet"))
            throw new ArgumentException("Supply an HTTP, HTTPS, FTP, eDonkey, or magnet URL.");

        JsonElement result = await _transport.JsonAsync(HttpMethod.Post, OfflineUrl("add_task", ("app_id", BaiduTransport.PanAppId),
            ("task_from", "0"), ("selected_idx", "1"), ("save_path", destination.TrimEnd('/') + "/"), ("source_url", source)),
            "offline task submission", false, cancellationToken);
        long id = result.Number("task_id");
        return id > 0 ? id : throw new InvalidDataException("Baidu did not return an offline task ID.");
    }

    public async Task<IReadOnlyList<OfflineTask>> ListOfflineTasksAsync(int page = 1, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        JsonElement result = await _transport.JsonAsync(HttpMethod.Post, OfflineUrl("list_task", ("need_task_info", "1"),
            ("status", "255"), ("start", BaiduTransport.Format((page - 1L) * 1000)), ("limit", "1000"),
            ("app_id", BaiduTransport.PanAppId)), "offline task listing", false, cancellationToken);
        return result.Items("task_info").Select(entry => ReadOfflineTask(entry, entry.Number("task_id"))).ToArray();
    }

    public async Task<IReadOnlyList<OfflineTask>> QueryOfflineTasksAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default)
    {
        long[] requested = ids.Distinct().ToArray();
        if (requested.Length == 0 || requested.Any(id => id <= 0))
            throw new ArgumentException("Specify positive offline task IDs.");

        var results = new List<OfflineTask>();
        foreach (long[] batch in requested.Chunk(100))
        {
            JsonElement request = await _transport.JsonAsync(HttpMethod.Get, OfflineUrl("query_task", ("app_id", BaiduTransport.PanAppId),
                ("op_type", "1"), ("task_ids", string.Join(',', batch.Select(BaiduTransport.Format)))),
                "offline task query", false, cancellationToken);
            JsonElement entries = request.Field("task_info");
            results.AddRange(batch.Select(id => ReadOfflineTask(entries.Field(BaiduTransport.Format(id)), id)));
        }

        return results;
    }

    public Task CancelOfflineTaskAsync(long id, CancellationToken cancellationToken = default) =>
        ChangeOfflineTaskAsync("cancel_task", "offline task cancellation", id, cancellationToken);

    public Task DeleteOfflineTaskAsync(long id, CancellationToken cancellationToken = default) =>
        ChangeOfflineTaskAsync("delete_task", "offline task deletion", id, cancellationToken);

    public async Task<long> ClearOfflineTasksAsync(CancellationToken cancellationToken = default)
    {
        JsonElement result = await _transport.JsonAsync(HttpMethod.Post, OfflineUrl("clear_task"), "offline task clearing", false,
            cancellationToken);
        return result.Number("total");
    }

    public async Task<IReadOnlyList<RecycleEntry>> ListRecycleBinAsync(int page = 1, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        JsonElement result = await _transport.JsonAsync(HttpMethod.Get, _transport.Pan("recycle/list", ("num", "100"),
            ("page", BaiduTransport.Format(page))), "recycle bin listing", true, cancellationToken);
        return result.Items("list").Select(entry => new RecycleEntry(ReadEntry(entry), entry.Number("leftTime"))).ToArray();
    }

    public async Task<bool> RestoreRecycleEntryAsync(long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        using MultipartFormDataContent form = BaiduTransport.JsonForm(new { list = new[] { new { fs_id = id } } });
        JsonElement result = await _transport.JsonAsync(HttpMethod.Post, _transport.Pcs("file", "restore"), "recycle restoration", false,
            cancellationToken, form);
        return result.Field("extra").Items("list").Any(entry => entry.Number("fs_id") == id);
    }

    public async Task DeleteRecycleEntryAsync(long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        await SendFormAsync(_transport.Pan("recycle/delete"), "recycle deletion", new() { ["fidlist"] = $"[{BaiduTransport.Format(id)}]" },
            cancellationToken);
    }

    public async Task<long> ClearRecycleBinAsync(CancellationToken cancellationToken = default)
    {
        JsonElement result = await _transport.JsonAsync(HttpMethod.Get, _transport.Pcs("file", "delete", ("type", "recycle")),
            "recycle bin clearing", false, cancellationToken, retryRead: false);
        return result.Field("extra").Number("succNum");
    }

    private Uri OfflineUrl(string operation, params (string Key, string Value)[] query) =>
        BaiduTransport.Build(_settings.UseHttps ? "https" : "http", BaiduTransport.PanHost, "/rest/2.0/services/cloud_dl",
            [("method", operation), .. query]);

    private async Task ChangeOfflineTaskAsync(string method, string operation, long id, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        await _transport.JsonAsync(HttpMethod.Post, OfflineUrl(method, ("app_id", BaiduTransport.PanAppId),
            ("task_id", BaiduTransport.Format(id))), operation, false, token);
    }

    private static OfflineTask ReadOfflineTask(JsonElement entry, long id) => new(id, entry.Number("status", -1),
        entry.Text("task_name"), entry.Text("save_path"), entry.Text("source_url"), entry.Number("file_size"),
        entry.Number("finished_size"), DateTimeOffset.FromUnixTimeSeconds(entry.Number("create_time")),
        DateTimeOffset.FromUnixTimeSeconds(entry.Number("start_time")), DateTimeOffset.FromUnixTimeSeconds(entry.Number("finish_time")),
        entry.Number("od_type"), entry.Number("result", entry.ValueKind == JsonValueKind.Undefined ? 1 : 0),
        entry.Items("file_list").Select(file => new OfflineFile(file.Text("file_name"), file.Number("file_size"))).ToArray());
}
