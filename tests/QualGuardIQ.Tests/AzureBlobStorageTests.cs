using System.Text;
using Microsoft.Extensions.Configuration;
using QualGuardIQ.Infrastructure.Storage;

namespace QualGuardIQ.Tests;

/// <summary>
/// Round trip against Azurite (the free local Azure Storage emulator). Runs when QMS_TEST_AZURE holds a connection
/// string, for example "UseDevelopmentStorage=true"; CI starts Azurite and sets it.
/// </summary>
public sealed class AzureBlobStorageTests
{
    [Fact]
    public async Task Stores_and_returns_files_in_a_private_container()
    {
        var connection = Environment.GetEnvironmentVariable("QMS_TEST_AZURE");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var storage = new AzureBlobFileStorage(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "AzureBlob", ["Storage:ConnectionString"] = connection, ["Storage:Container"] = "qms-test"
        }).Build());
        await storage.EnsureReadyAsync(default);
        await storage.EnsureReadyAsync(default); // idempotent
        var key = $"t/{Guid.NewGuid():N}/procedure.pdf";
        var body = Encoding.UTF8.GetBytes("%PDF-1.4 controlled copy");
        await storage.PutAsync(key, new MemoryStream(body), "application/pdf", default);
        await using var s = await storage.GetAsync(key, default);
        using var copy = new MemoryStream();
        await s.CopyToAsync(copy);
        Assert.Equal(body, copy.ToArray());

        var anonymous = new Azure.Storage.Blobs.BlobContainerClient(new Uri(new Azure.Storage.Blobs.BlobServiceClient(connection).Uri, "qms-test"));
        await Assert.ThrowsAsync<Azure.RequestFailedException>(() => anonymous.GetBlobClient(key).DownloadContentAsync());
    }

    [Fact]
    public void Requires_a_connection_string()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Provider"] = "AzureBlob" }).Build();
        Assert.Throws<InvalidOperationException>(() => new AzureBlobFileStorage(config));
    }
}
