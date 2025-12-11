using System;
using System.Collections.Generic;

namespace FileDownloader.Models
{
    public enum DownloadStatus
    {
        Pending,
        Downloading,
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    public class DownloadItem
    {
        public int Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public string SavePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public long DownloadedBytes { get; set; }
        public DownloadStatus Status { get; set; }
        public int ThreadCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> Tags { get; set; } = new List<string>();

        public double Progress => TotalBytes > 0 ? (double)DownloadedBytes / TotalBytes * 100 : 0;
        public string StatusText => Status switch
        {
            DownloadStatus.Pending => "Ожидание",
            DownloadStatus.Downloading => "Загрузка",
            DownloadStatus.Paused => "Приостановлено",
            DownloadStatus.Completed => "Завершено",
            DownloadStatus.Failed => "Ошибка",
            DownloadStatus.Cancelled => "Отменено",
            _ => "Неизвестно"
        };
    }
}

