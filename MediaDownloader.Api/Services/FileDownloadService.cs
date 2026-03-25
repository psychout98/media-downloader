namespace MediaDownloader.Api.Services;

public class FileDownloadService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FileDownloadService> _logger;

    private const int BufferSize = 65536; // 64KB chunks

    public FileDownloadService(IHttpClientFactory httpClientFactory, ILogger<FileDownloadService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task DownloadFileAsync(
        string url,
        string destPath,
        IProgress<(long downloaded, long total)>? progress = null,
        CancellationToken ct = default)
    {
        var destDir = Path.GetDirectoryName(destPath);
        if (destDir != null) Directory.CreateDirectory(destDir);

        var client = _httpClientFactory.CreateClient("download");

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        _logger.LogInformation("Downloading {Url} ({Size} bytes) → {Dest}", url, totalBytes, destPath);

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        var buffer = new byte[BufferSize];
        long downloaded = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloaded += bytesRead;
            progress?.Report((downloaded, totalBytes));
        }

        _logger.LogInformation("Download complete: {Dest} ({Downloaded} bytes)", destPath, downloaded);
    }
}
