using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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

    public class DownloadItem : INotifyPropertyChanged
    {
        private long _totalBytes;
        private long _downloadedBytes;
        private DownloadStatus _status;

        public int Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public string SavePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        
        public long TotalBytes
        {
            get => _totalBytes;
            set
            {
                if (_totalBytes != value)
                {
                    _totalBytes = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Progress));
                }
            }
        }
        
        public long DownloadedBytes
        {
            get => _downloadedBytes;
            set
            {
                if (_downloadedBytes != value)
                {
                    _downloadedBytes = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Progress));
                }
            }
        }
        
        public DownloadStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }
        
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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

