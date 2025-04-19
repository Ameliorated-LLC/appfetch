using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace AME.AppFetch.Services;

public class HttpProgressClient : IDisposable
{
    private string _downloadUrl = null!;
    private string _destinationFilePath = null!;

    public HttpClient Client;

    public delegate void ProgressChangedHandler(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage);

    public event ProgressChangedHandler? ProgressChanged;

    public HttpProgressClient()
    {
        Client = new HttpClient { Timeout = TimeSpan.FromDays(1) };
    }

    public async Task StartDownload(string downloadUrl, string destinationFilePath, long? size = null)
    {
        _downloadUrl = downloadUrl;
        _destinationFilePath = destinationFilePath;

        for (int i = 0; i < 3; i++)
        {
            await Task.Delay(1000 * i);
            using (var response = await Client.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                {
                    i = 2;
                    continue;
                }

                await DownloadFileFromHttpResponseMessage(response, size);
                return;
            }
        }

        throw new Exception("Unexpected end of StartDownload.");
    }

    public Task<HttpResponseMessage> GetAsync(string link)
    {
        return Client.GetAsync(link);
    }

    private async Task DownloadFileFromHttpResponseMessage(HttpResponseMessage response, long? size)
    {
        response.EnsureSuccessStatusCode();

        if (!response.Content.Headers.ContentLength.HasValue)
            size = response.Content.Headers.ContentLength;

        using (var contentStream = await response.Content.ReadAsStreamAsync())
            await ProcessContentStream(size, contentStream);
    }

    private async Task ProcessContentStream(long? totalDownloadSize, Stream contentStream)
    {
        var totalBytesRead = 0L;
        var readCount = 0L;
        var buffer = new byte[8192];
        var isMoreToRead = true;

        using (var fileStream = new FileStream(_destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
        {
            do
            {
                var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    isMoreToRead = false;
                    TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                    continue;
                }

                await fileStream.WriteAsync(buffer, 0, bytesRead);

                totalBytesRead += bytesRead;
                readCount += 1;

                if (readCount % 50 == 0)
                    TriggerProgressChanged(totalDownloadSize, totalBytesRead);
            } while (isMoreToRead);
        }
    }

    private void TriggerProgressChanged(long? totalDownloadSize, long totalBytesRead)
    {
        if (ProgressChanged == null)
            return;

        double? progressPercentage = null;
        if (totalDownloadSize.HasValue)
        {
            progressPercentage = Math.Min(Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2), 100);
        }


        ProgressChanged(totalDownloadSize, totalBytesRead, progressPercentage);
    }

    public void Dispose()
    {
        Client?.Dispose();
    }
}