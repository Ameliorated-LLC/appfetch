using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AME.AppFetch;
using AME.Core;

namespace AME.AppFetch
{
    public class Update
    {
        public static async Task<string?> CheckForUpdate()
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("curl/7.55.1");

                    string releasesUrl = "https://api.github.com/repos/Ameliorated-LLC/appfetch/releases";
                    var response = await httpClient.GetAsync(releasesUrl);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(json);
                    var release = doc.RootElement.EnumerateArray().FirstOrDefault();
                    
                    var tag = release.GetProperty("tag_name").GetString()!.TrimStart('v');

                    var versionNumber = GetVersionNumber(tag);

                    if (versionNumber > GetVersionNumber(Program.Version))
                        return tag;
                    return null;
                }
            }
            catch (Exception e)
            {
                return null;
            }
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct COORD
        {
            public short X;
            public short Y;

            public COORD(short X, short Y)
            {
                this.X = X;
                this.Y = Y;
            }
        };

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool WriteConsoleOutputCharacter(IntPtr hConsoleOutput, StringBuilder lpCharacter,
            uint nLength, COORD dwWriteCoord, out uint lpNumberOfCharsWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int nStdHandle);

        public static async Task<bool> InstallUpdate(BackgroundWorker BackgroundWorker)
        {
            if (BackgroundWorker == null)
                throw new Exception("InstallUpdate was called with no BackgroundWorker specified.");
            BackgroundWorker.WorkerReportsProgress = true;

            BackgroundWorker.ReportProgress(5);

            var dest = Environment.ExpandEnvironmentVariables(@"%TEMP%\App Fetch Update" + ".exe");
            // Download/install update
            using (var httpClient = new HttpProgressClient())
            {
                string downloadUrl;
                long size = 41000000;

                try
                {
                    httpClient.Client.DefaultRequestHeaders.UserAgent.ParseAdd("curl/7.55.1");

                    string releasesUrl = "https://api.github.com/repos/Ameliorated-LLC/appfetch/releases";
                    var response = await httpClient.GetAsync(releasesUrl);
                    response.EnsureSuccessStatusCode();

                    var releasesContent = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(releasesContent);
                    var release = doc.RootElement.EnumerateArray().FirstOrDefault();

                    downloadUrl = null;

                    if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        var specificAsset = assets.EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString().Contains("appfetch") && a.GetProperty("name").GetString().EndsWith(".exe"));
                        var fallbackAsset = assets.EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString().EndsWith(".exe"));
                        var asset = specificAsset.ValueKind != JsonValueKind.Undefined ? specificAsset : fallbackAsset;
                        if (asset.ValueKind != JsonValueKind.Undefined)
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? throw new Exception("GitHub download link unavailable.");
                            if (asset.TryGetProperty("size", out var sizeProp))
                            {
                                long.TryParse(sizeProp.ToString(), out size);
                            }
                        }
                    }

                    if (downloadUrl == null)
                        throw new Exception("GitHub link unavailable.");
                }
                catch (Exception e)
                {
                    throw new Exception("Update check failed: " + e.Message);
                    return false;
                }

                BackgroundWorker.ReportProgress(10);

                if (downloadUrl == null)
                    throw new Exception("Download link unavailable.");

                httpClient.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) =>
                {
                    if (progressPercentage.HasValue)
                        BackgroundWorker.ReportProgress((int)Math.Ceiling(10 + (progressPercentage.Value * 0.9)));
                };

                try
                {
                    if (File.Exists(dest))
                        File.Delete(dest);
                }
                catch
                {
                }
                try
                {
                    await httpClient.StartDownload(downloadUrl, dest, size);
                }
                catch (Exception e)
                {
                    throw new Exception("Download failed: " + e.Message);
                    return false;
                }

                BackgroundWorker.ReportProgress(100);

                try
                {
                    var fileInfo = new FileInfo(dest);
                    FileSecurity fileSecurity = fileInfo.GetAccessControl();
                    fileSecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), 
                        FileSystemRights.Read | FileSystemRights.ExecuteFile, AccessControlType.Allow));

                    fileInfo.SetAccessControl(fileSecurity);
                }
                catch (Exception e)
                {
                    try
                    {
                        if (File.Exists(dest))
                            File.Delete(dest);
                    }
                    catch
                    {
                    }

                    throw new Exception(e.Message);
                }
            }

            try
            {
                Process.Start(new ProcessStartInfo(dest, $"--update \"{Win32.ProcessEx.GetCurrentProcessFileLocation()}\"")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
                Environment.Exit(0);
            }
            catch (Exception e)
            {
                throw new Exception("Update process start failed: " + e.Message);
                return false;
            }

            Process.GetCurrentProcess().Kill();
            return true;
        }

        public static decimal GetVersionNumber(string toBeParsed)
        {
            // Examples:
            // 0.4
            // 0.4 Alpha
            // 1.0.5
            // 1.0.5 Beta


            // Remove characters after first space (and the space itself)
            if (toBeParsed.IndexOf(' ') >= 0)
                toBeParsed = toBeParsed.Substring(0, toBeParsed.IndexOf(' '));

            if (toBeParsed.LastIndexOf('.') != toBeParsed.IndexOf('.'))
            {
                // Example: 1.0.5
                toBeParsed = toBeParsed.Remove(toBeParsed.LastIndexOf('.'), 1);
                // Result: 1.05
            }

            return decimal.Parse(toBeParsed, CultureInfo.InvariantCulture);
        }

        public class HttpProgressClient : IDisposable
        {
            private string _downloadUrl;
            private string _destinationFilePath;

            public HttpClient Client;

            public delegate void ProgressChangedHandler(long? totalFileSize, long totalBytesDownloaded,
                double? progressPercentage);

            public event ProgressChangedHandler ProgressChanged;

            public HttpProgressClient()
            {
                Client = new HttpClient { Timeout = TimeSpan.FromDays(1) };
            }

            public async Task StartDownload(string downloadUrl, string destinationFilePath, long? size = null)
            {
                _downloadUrl = downloadUrl;
                _destinationFilePath = destinationFilePath;

                using (var response = await Client.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    await DownloadFileFromHttpResponseMessage(response, size);
            }

            public Task<HttpResponseMessage> GetAsync(string link)
            {
                return Client.GetAsync(link);
            }

            private async Task DownloadFileFromHttpResponseMessage(HttpResponseMessage response, long? size)
            {
                response.EnsureSuccessStatusCode();

                if (!size.HasValue)
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

                using (var fileStream = new FileStream(_destinationFilePath, FileMode.Create, FileAccess.Write,
                           FileShare.None, 8192, true))
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
                    progressPercentage = Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2);
                }


                ProgressChanged(totalDownloadSize, totalBytesRead, progressPercentage);
            }

            public void Dispose()
            {
                Client?.Dispose();
            }
        }
    }
}