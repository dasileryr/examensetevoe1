using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FileDownloader.Models;

namespace FileDownloader.Services
{
    public class DownloadService
    {
        private readonly Dictionary<int, CancellationTokenSource> _cancellationTokens = new();
        private readonly Dictionary<int, DownloadTask> _activeDownloads = new();
        private readonly DatabaseService _databaseService;

        public event EventHandler<DownloadItem>? DownloadProgress;
        public event EventHandler<DownloadItem>? DownloadCompleted;

        public DownloadService(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public async Task StartDownloadAsync(DownloadItem item)
        {
            if (item.Status == DownloadStatus.Downloading)
                return;

            var cts = new CancellationTokenSource();
            _cancellationTokens[item.Id] = cts;
            _activeDownloads[item.Id] = new DownloadTask { Item = item, CancellationToken = cts.Token };

            item.Status = DownloadStatus.Downloading;
            _databaseService.UpdateDownload(item);

            try
            {
                await DownloadFileAsync(item, cts.Token);
            }
            catch (Exception ex)
            {
                item.Status = DownloadStatus.Failed;
                item.ErrorMessage = ex.Message;
                _databaseService.UpdateDownload(item);
                DownloadCompleted?.Invoke(this, item);
            }
            finally
            {
                _cancellationTokens.Remove(item.Id);
                _activeDownloads.Remove(item.Id);
            }
        }

        private async Task DownloadFileAsync(DownloadItem item, CancellationToken cancellationToken)
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromHours(1);

            var response = await httpClient.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            if (item.TotalBytes == 0)
            {
                item.TotalBytes = response.Content.Headers.ContentLength ?? 0;
                _databaseService.UpdateDownload(item);
            }

            var totalBytes = item.TotalBytes;
            var threadCount = Math.Min(item.ThreadCount, 10); // Ограничение до 10 потоков

            bool supportsRangeRequests = response.Headers.AcceptRanges != null && 
                                         response.Headers.AcceptRanges.Contains("bytes");

            if (totalBytes > 0 && threadCount > 1 && supportsRangeRequests)
            {
                await DownloadMultiThreadedAsync(item, httpClient, totalBytes, threadCount, cancellationToken);
            }
            else
            {
                await DownloadSingleThreadedAsync(item, httpClient, cancellationToken);
            }

            item.Status = DownloadStatus.Completed;
            item.CompletedAt = DateTime.Now;
            _databaseService.UpdateDownload(item);
            DownloadCompleted?.Invoke(this, item);
        }

        private async Task DownloadSingleThreadedAsync(DownloadItem item, HttpClient httpClient, CancellationToken cancellationToken)
        {
            var fullPath = Path.Combine(item.SavePath, item.FileName);
            Directory.CreateDirectory(item.SavePath);

            using var response = await httpClient.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            FileMode fileMode = item.DownloadedBytes > 0 ? FileMode.Open : FileMode.Create;
            using var fileStream = new FileStream(fullPath, fileMode, FileAccess.Write, FileShare.None);
            
            if (item.DownloadedBytes > 0)
            {
                fileStream.Seek(item.DownloadedBytes, SeekOrigin.Begin);
            }

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

            var buffer = new byte[8192];
            long totalRead = item.DownloadedBytes;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (read == 0) break;

                await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                totalRead += read;
                item.DownloadedBytes = totalRead;
                
                if (totalRead % 65536 == 0) // Обновляем каждые 64KB
                {
                    _databaseService.UpdateDownload(item);
                    DownloadProgress?.Invoke(this, item);
                }
            }
            
            item.DownloadedBytes = totalRead;
            _databaseService.UpdateDownload(item);
            DownloadProgress?.Invoke(this, item);
        }

        private async Task DownloadMultiThreadedAsync(DownloadItem item, HttpClient httpClient, long totalBytes, int threadCount, CancellationToken cancellationToken)
        {
            var fullPath = Path.Combine(item.SavePath, item.FileName);
            Directory.CreateDirectory(item.SavePath);

            var chunkSize = totalBytes / threadCount;
            var tasks = new List<Task>();

            using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Write);

            for (int i = 0; i < threadCount; i++)
            {
                var start = i * chunkSize;
                var end = i == threadCount - 1 ? totalBytes - 1 : (i + 1) * chunkSize - 1;

                tasks.Add(DownloadChunkAsync(item, httpClient, fileStream, start, end, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        private async Task DownloadChunkAsync(DownloadItem item, HttpClient httpClient, FileStream fileStream, long start, long end, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

            var buffer = new byte[8192];
            long position = start;
            long lastUpdate = 0;

            while (position <= end)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (read == 0) break;

                lock (fileStream)
                {
                    fileStream.Seek(position, SeekOrigin.Begin);
                    fileStream.Write(buffer, 0, read);
                }

                position += read;
                
                // Обновляем прогресс каждые 64KB на поток
                if (position - lastUpdate >= 65536)
                {
                    lock (item)
                    {
                        item.DownloadedBytes = Math.Max(item.DownloadedBytes, position);
                    }
                    _databaseService.UpdateDownload(item);
                    DownloadProgress?.Invoke(this, item);
                    lastUpdate = position;
                }
            }
            
            // Финальное обновление
            lock (item)
            {
                item.DownloadedBytes = Math.Max(item.DownloadedBytes, position);
            }
            _databaseService.UpdateDownload(item);
            DownloadProgress?.Invoke(this, item);
        }

        public void PauseDownload(int id)
        {
            if (_cancellationTokens.ContainsKey(id))
            {
                _cancellationTokens[id].Cancel();
                var item = _activeDownloads[id].Item;
                item.Status = DownloadStatus.Paused;
                _databaseService.UpdateDownload(item);
            }
        }

        public void StopDownload(int id)
        {
            if (_cancellationTokens.ContainsKey(id))
            {
                _cancellationTokens[id].Cancel();
                var item = _activeDownloads[id].Item;
                item.Status = DownloadStatus.Cancelled;
                _databaseService.UpdateDownload(item);
            }
        }

        public void ResumeDownload(int id)
        {
            var item = _databaseService.GetAllDownloads().FirstOrDefault(d => d.Id == id);
            if (item != null && item.Status == DownloadStatus.Paused)
            {
                _ = StartDownloadAsync(item);
            }
        }

        private class DownloadTask
        {
            public DownloadItem Item { get; set; } = null!;
            public CancellationToken CancellationToken { get; set; }
        }
    }
}

