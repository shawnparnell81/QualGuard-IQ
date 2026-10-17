using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;

namespace QualGuardIQ.Infrastructure.Storage;

/// <summary>
/// Azure Blob Storage provider (optional). Enable with Storage:Provider = "AzureBlob", Storage:ConnectionString and
/// Storage:Container. The container is private; files are only served through the API.
/// </summary>
public sealed class AzureBlobFileStorage : IFileStorage
{
    private readonly BlobContainerClient _container;

    public AzureBlobFileStorage(IConfiguration config)
    {
        var s = config.GetSection("Storage");
        var connection = s["ConnectionString"];
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Storage:ConnectionString is required for the AzureBlob storage provider.");
        _container = new BlobContainerClient(connection, s["Container"] ?? "qms-files");
    }

    public async Task EnsureReadyAsync(CancellationToken ct) =>
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct) =>
        await _container.GetBlobClient(key).UploadAsync(content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);

    public async Task<Stream> GetAsync(string key, CancellationToken ct) =>
        (await _container.GetBlobClient(key).DownloadStreamingAsync(cancellationToken: ct)).Value.Content;
}
