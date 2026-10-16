using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace QualGuardIQ.Infrastructure.Storage;

public interface IFileStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<Stream> GetAsync(string key, CancellationToken ct);
    Task EnsureReadyAsync(CancellationToken ct);
}

/// <summary>S3-compatible storage. MinIO by default for local and self-hosted use; Amazon S3 by changing ServiceUrl/credentials.</summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;

    public S3FileStorage(IConfiguration config)
    {
        var s = config.GetSection("Storage");
        _bucket = s["Bucket"] ?? "qms-files";
        var cfg = new AmazonS3Config
        {
            ForcePathStyle = s.GetValue("ForcePathStyle", true),
            AuthenticationRegion = s["Region"] ?? "us-east-1",
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };
        if (!string.IsNullOrWhiteSpace(s["ServiceUrl"])) cfg.ServiceURL = s["ServiceUrl"];
        else cfg.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(s["Region"] ?? "us-east-1");
        _client = string.IsNullOrWhiteSpace(s["AccessKey"])
            ? new AmazonS3Client(cfg)
            : new AmazonS3Client(new BasicAWSCredentials(s["AccessKey"], s["SecretKey"]), cfg);
    }

    public async Task EnsureReadyAsync(CancellationToken ct)
    {
        try { await _client.PutBucketAsync(new PutBucketRequest { BucketName = _bucket }, ct); }
        catch (AmazonS3Exception e) when (e.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists") { }
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct) =>
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket, Key = key, InputStream = content, ContentType = contentType, AutoCloseStream = false,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.None
        }, ct);

    public async Task<Stream> GetAsync(string key, CancellationToken ct)
    {
        var r = await _client.GetObjectAsync(_bucket, key, ct);
        return r.ResponseStream;
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>Integration point for an antivirus engine (for example ClamAV). The default marks files NotScanned.</summary>
public interface IMalwareScanner
{
    Task<Domain.Shared.ScanStatus> ScanAsync(Stream content, string fileName, CancellationToken ct);
}

public sealed class NoMalwareScanner : IMalwareScanner
{
    public Task<Domain.Shared.ScanStatus> ScanAsync(Stream content, string fileName, CancellationToken ct) =>
        Task.FromResult(Domain.Shared.ScanStatus.NotScanned);
}
