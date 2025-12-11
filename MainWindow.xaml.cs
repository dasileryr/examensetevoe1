using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FileDownloader.Models;
using FileDownloader.Services;
using Microsoft.Win32;

namespace FileDownloader
{
    public partial class MainWindow : Window
    {
        private readonly DatabaseService _databaseService;
        private readonly DownloadService _downloadService;
        private readonly FileService _fileService;
        private DownloadItem? _selectedItem;

        public MainWindow()
        {
            InitializeComponent();
            _databaseService = new DatabaseService();
            _downloadService = new DownloadService(_databaseService);
            _fileService = new FileService(_databaseService);

            _downloadService.DownloadProgress += (s, e) => Dispatcher.Invoke(() => RefreshDownloads());
            _downloadService.DownloadCompleted += (s, e) => Dispatcher.Invoke(() => RefreshDownloads());

            RefreshDownloads();
        }

        private void AddDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlTextBox.Text.Trim();
            var savePath = SavePathTextBox.Text.Trim();
            var threadCountText = ThreadCountTextBox.Text.Trim();
            var tagsText = TagsTextBox.Text.Trim();

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(savePath))
            {
                MessageBox.Show("Пожалуйста, укажите URL и путь сохранения.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(threadCountText, out int threadCount) || threadCount < 1)
            {
                threadCount = 4;
            }

            if (threadCount > 10) threadCount = 10;

            try
            {
                var fileName = Path.GetFileName(url);
                if (string.IsNullOrEmpty(fileName) || fileName == "/")
                {
                    fileName = $"download_{DateTime.Now:yyyyMMddHHmmss}";
                }

                var tags = tagsText.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                var item = new DownloadItem
                {
                    Url = url,
                    SavePath = savePath,
                    FileName = fileName,
                    ThreadCount = threadCount,
                    Status = DownloadStatus.Pending,
                    CreatedAt = DateTime.Now,
                    Tags = tags
                };

                item.Id = _databaseService.AddDownload(item);
                RefreshDownloads();

                _ = _downloadService.StartDownloadAsync(item);

                // Очистка полей
                UrlTextBox.Clear();
                TagsTextBox.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении загрузки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SavePathTextBox.Text = dialog.SelectedPath;
            }
        }

        private void DownloadsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var listBox = sender as ListBox;
            _selectedItem = listBox?.SelectedItem as DownloadItem;

            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            var hasSelection = _selectedItem != null;
            var isActive = hasSelection && (_selectedItem!.Status == DownloadStatus.Downloading || _selectedItem.Status == DownloadStatus.Pending);
            var isPaused = hasSelection && _selectedItem!.Status == DownloadStatus.Paused;
            var isCompleted = hasSelection && _selectedItem!.Status == DownloadStatus.Completed;

            PauseButton.IsEnabled = hasSelection && isActive;
            ResumeButton.IsEnabled = hasSelection && isPaused;
            StopButton.IsEnabled = hasSelection && isActive;
            DeleteButton.IsEnabled = hasSelection;
            RenameButton.IsEnabled = hasSelection && isCompleted;
            MoveButton.IsEnabled = hasSelection && isCompleted;
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                _downloadService.PauseDownload(_selectedItem.Id);
                RefreshDownloads();
            }
        }

        private void ResumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                _downloadService.ResumeDownload(_selectedItem.Id);
                RefreshDownloads();
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                _downloadService.StopDownload(_selectedItem.Id);
                RefreshDownloads();
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                var result = MessageBox.Show("Вы уверены, что хотите удалить эту загрузку?", "Подтверждение", 
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    if (_selectedItem.Status == DownloadStatus.Downloading || _selectedItem.Status == DownloadStatus.Pending)
                    {
                        _downloadService.StopDownload(_selectedItem.Id);
                    }
                    _fileService.DeleteFile(_selectedItem);
                    _selectedItem = null;
                    RefreshDownloads();
                }
            }
        }

        private void RenameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                var dialog = new InputDialog("Переименовать файл", "Введите новое имя файла:", _selectedItem.FileName);
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Answer))
                {
                    if (_fileService.RenameFile(_selectedItem, dialog.Answer))
                    {
                        MessageBox.Show("Файл успешно переименован.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                        RefreshDownloads();
                    }
                    else
                    {
                        MessageBox.Show("Ошибка при переименовании файла.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void MoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog();
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog();
                if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if (_fileService.MoveFile(_selectedItem, folderDialog.SelectedPath))
                    {
                        MessageBox.Show("Файл успешно перемещён.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                        RefreshDownloads();
                    }
                    else
                    {
                        MessageBox.Show("Ошибка при перемещении файла.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void SearchTagsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshDownloads();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTagsTextBox.Clear();
            RefreshDownloads();
        }

        private void RefreshDownloads()
        {
            List<DownloadItem> downloads;

            var searchText = SearchTagsTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(searchText))
            {
                var tags = searchText.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();
                downloads = _databaseService.SearchByTags(tags);
            }
            else
            {
                downloads = _databaseService.GetAllDownloads();
            }

            AllDownloadsListBox.ItemsSource = downloads;
            ActiveDownloadsListBox.ItemsSource = downloads.Where(d => 
                d.Status == DownloadStatus.Downloading || 
                d.Status == DownloadStatus.Pending || 
                d.Status == DownloadStatus.Paused).ToList();
            CompletedDownloadsListBox.ItemsSource = downloads.Where(d => d.Status == DownloadStatus.Completed).ToList();
            FailedDownloadsListBox.ItemsSource = downloads.Where(d => 
                d.Status == DownloadStatus.Failed || 
                d.Status == DownloadStatus.Cancelled).ToList();

            UpdateButtonStates();
        }
    }
}

