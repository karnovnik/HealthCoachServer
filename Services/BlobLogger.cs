using Azure.Storage.Blobs;
using System.Text;
using System.Text.Json;


namespace HealthCoachServer.Services;
public class BlobLogger
{
    private readonly IConfiguration _cfg;
    public bool IsConfigured { get; }
    public BlobLogger(IConfiguration cfg) { _cfg = cfg; IsConfigured = !string.IsNullOrEmpty(_cfg["LOG_BLOB_CONNECTION_STRING"]) && !string.IsNullOrEmpty(_cfg["LOG_BLOB_CONTAINER"]); }


    public async Task<string> SaveRawAsync(string requestId, string userId, string imageId, long receivedBytes, string rawResponse)
    {
        var conn = _cfg["LOG_BLOB_CONNECTION_STRING"]!;
        var container = _cfg["LOG_BLOB_CONTAINER"]!;
        var client = new BlobContainerClient(conn, container);
        await client.CreateIfNotExistsAsync();
        var path = $"llm_raw/{DateTime.UtcNow:yyyyMMdd}/{imageId}_{DateTime.UtcNow:HHmmss}.json";
        var blob = client.GetBlobClient(path);
        var meta = new { requestId, userId, imageId, receivedBytes, timestamp = DateTime.UtcNow };
        var payload = JsonSerializer.Serialize(new { meta, rawResponse });
        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        await blob.UploadAsync(ms, overwrite: true);
        return path;
    }
}