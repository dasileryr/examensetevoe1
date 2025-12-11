using System;
using System.IO;
using FileDownloader.Models;

namespace FileDownloader.Services
{
    public class FileService
    {
        private readonly DatabaseService _databaseService;

        public FileService(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public bool DeleteFile(DownloadItem item)
        {
            try
            {
                var fullPath = Path.Combine(item.SavePath, item.FileName);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
                _databaseService.DeleteDownload(item.Id);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RenameFile(DownloadItem item, string newName)
        {
            try
            {
                var oldPath = Path.Combine(item.SavePath, item.FileName);
                var newPath = Path.Combine(item.SavePath, newName);

                if (File.Exists(oldPath))
                {
                    File.Move(oldPath, newPath);
                }

                item.FileName = newName;
                _databaseService.UpdateDownload(item);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool MoveFile(DownloadItem item, string newPath)
        {
            try
            {
                var oldFullPath = Path.Combine(item.SavePath, item.FileName);
                var newFullPath = Path.Combine(newPath, item.FileName);

                if (File.Exists(oldFullPath))
                {
                    Directory.CreateDirectory(newPath);
                    File.Move(oldFullPath, newFullPath);
                }

                item.SavePath = newPath;
                _databaseService.UpdateDownload(item);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

