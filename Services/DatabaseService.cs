using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using FileDownloader.Models;

namespace FileDownloader.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly string _dbPath = "downloads.db";

        public DatabaseService()
        {
            _connectionString = $"Data Source={_dbPath};Version=3;";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            if (!File.Exists(_dbPath))
            {
                SQLiteConnection.CreateFile(_dbPath);
            }

            using var connection = new SQLiteConnection(_connectionString);
            connection.Open();

            var createDownloadsTable = @"
                CREATE TABLE IF NOT EXISTS Downloads (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Url TEXT NOT NULL,
                    SavePath TEXT NOT NULL,
                    FileName TEXT NOT NULL,
                    TotalBytes INTEGER DEFAULT 0,
                    DownloadedBytes INTEGER DEFAULT 0,
                    Status INTEGER NOT NULL,
                    ThreadCount INTEGER DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    CompletedAt TEXT,
                    ErrorMessage TEXT
                )";

            var createTagsTable = @"
                CREATE TABLE IF NOT EXISTS Tags (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DownloadId INTEGER NOT NULL,
                    Tag TEXT NOT NULL,
                    FOREIGN KEY (DownloadId) REFERENCES Downloads(Id) ON DELETE CASCADE
                )";

            using (var command = new SQLiteCommand(createDownloadsTable, connection))
            {
                command.ExecuteNonQuery();
            }

            using (var command = new SQLiteCommand(createTagsTable, connection))
            {
                command.ExecuteNonQuery();
            }
        }

        public int AddDownload(DownloadItem item)
        {
            using var connection = new SQLiteConnection(_connectionString);
            connection.Open();

            var insertQuery = @"
                INSERT INTO Downloads (Url, SavePath, FileName, TotalBytes, DownloadedBytes, Status, ThreadCount, CreatedAt, CompletedAt, ErrorMessage)
                VALUES (@Url, @SavePath, @FileName, @TotalBytes, @DownloadedBytes, @Status, @ThreadCount, @CreatedAt, @CompletedAt, @ErrorMessage);
                SELECT last_insert_rowid();";

            using var command = new SQLiteCommand(insertQuery, connection);
            command.Parameters.AddWithValue("@Url", item.Url);
            command.Parameters.AddWithValue("@SavePath", item.SavePath);
            command.Parameters.AddWithValue("@FileName", item.FileName);
            command.Parameters.AddWithValue("@TotalBytes", item.TotalBytes);
            command.Parameters.AddWithValue("@DownloadedBytes", item.DownloadedBytes);
            command.Parameters.AddWithValue("@Status", (int)item.Status);
            command.Parameters.AddWithValue("@ThreadCount", item.ThreadCount);
            command.Parameters.AddWithValue("@CreatedAt", item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@CompletedAt", item.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@ErrorMessage", item.ErrorMessage ?? (object)DBNull.Value);

            var id = Convert.ToInt32(command.ExecuteScalar());

            if (item.Tags.Any())
            {
                AddTags(id, item.Tags);
            }

            return id;
        }

        public void UpdateDownload(DownloadItem item)
        {
            using var connection = new SQLiteConnection(_connectionString);
            connection.Open();

            var updateQuery = @"
                UPDATE Downloads 
                SET Url = @Url, SavePath = @SavePath, FileName = @FileName, 
                    TotalBytes = @TotalBytes, DownloadedBytes = @DownloadedBytes, 
                    Status = @Status, ThreadCount = @ThreadCount, 
                    CompletedAt = @CompletedAt, ErrorMessage = @ErrorMessage
                WHERE Id = @Id";

            using var command = new SQLiteCommand(updateQuery, connection);
            command.Parameters.AddWithValue("@Id", item.Id);
            command.Parameters.AddWithValue("@Url", item.Url);
            command.Parameters.AddWithValue("@SavePath", item.SavePath);
            command.Parameters.AddWithValue("@FileName", item.FileName);
            command.Parameters.AddWithValue("@TotalBytes", item.TotalBytes);
            command.Parameters.AddWithValue("@DownloadedBytes", item.DownloadedBytes);
            command.Parameters.AddWithValue("@Status", (int)item.Status);
            command.Parameters.AddWithValue("@ThreadCount", item.ThreadCount);
            command.Parameters.AddWithValue("@CompletedAt", item.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@ErrorMessage", item.ErrorMessage ?? (object)DBNull.Value);

            command.ExecuteNonQuery();
        }

        public void DeleteDownload(int id)
        {
            using var connection = new SQLiteConnection(_connectionString);
            connection.Open();

            var deleteTagsQuery = "DELETE FROM Tags WHERE DownloadId = @Id";
            using (var command = new SQLiteCommand(deleteTagsQuery, connection))
            {
                command.Parameters.AddWithValue("@Id", id);
                command.ExecuteNonQuery();
            }

            var deleteDownloadQuery = "DELETE FROM Downloads WHERE Id = @Id";
            using (var command = new SQLiteCommand(deleteDownloadQuery, connection))
            {
                command.Parameters.AddWithValue("@Id", id);
                command.ExecuteNonQuery();
            }
        }

        public List<DownloadItem> GetAllDownloads()
        {
            var downloads = new List<DownloadItem>();

            using var connection = new SQLiteConnection(_connectionString);
            connection.Open();

            var query = "SELECT * FROM Downloads ORDER BY CreatedAt DESC";
            using var command = new SQLiteCommand(query, connection);
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var item = new DownloadItem
                {
                    Id = reader.GetInt32(0),
                    Url = reader.GetString(1),
                    SavePath = reader.GetString(2),
                    FileName = reader.GetString(3),
                    TotalBytes = reader.GetInt64(4),
                    DownloadedBytes = reader.GetInt64(5),
                    Status = (DownloadStatus)reader.GetInt32(6),
                    ThreadCount = reader.GetInt32(7),
                    CreatedAt = DateTime.Parse(reader.GetString(8)),
                    CompletedAt = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9)),
                    ErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10)
                };

                item.Tags = GetTags(item.Id);
                downloads.Add(item);
            }

            return downloads;
        }

        public List<DownloadItem> SearchByTags(List<string> tags)
        {
            if (!tags.Any()) return GetAllDownloads();

            var downloads = new List<DownloadItem>();
            var tagPlaceholders = string.Join(",", tags.Select((_, i) => $"@tag{i}"));

            using var connection = new SQLiteConnection(_connectionString);
            connection.Open();

            var query = $@"
                SELECT DISTINCT d.* FROM Downloads d
                INNER JOIN Tags t ON d.Id = t.DownloadId
                WHERE t.Tag IN ({tagPlaceholders})
                ORDER BY d.CreatedAt DESC";

            using var command = new SQLiteCommand(query, connection);
            for (int i = 0; i < tags.Count; i++)
            {
                command.Parameters.AddWithValue($"@tag{i}", tags[i]);
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var item = new DownloadItem
                {
                    Id = reader.GetInt32(0),
                    Url = reader.GetString(1),
                    SavePath = reader.GetString(2),
                    FileName = reader.GetString(3),
                    TotalBytes = reader.GetInt64(4),
                    DownloadedBytes = reader.GetInt64(5),
                    Status = (DownloadStatus)reader.GetInt32(6),
                    ThreadCount = reader.GetInt32(7),
                    CreatedAt = DateTime.Parse(reader.GetString(8)),
                    CompletedAt = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9)),
                    ErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10)
                };

                item.Tags = GetTags(item.Id);
                downloads.Add(item);
            }

            return downloads;
        }

        private void AddTags(int downloadId, List<string> tags)
        {
            using var connection = new SQLiteConnection(_connectionString);
            connection.Open();

            foreach (var tag in tags)
            {
                var insertQuery = "INSERT INTO Tags (DownloadId, Tag) VALUES (@DownloadId, @Tag)";
                using var command = new SQLiteCommand(insertQuery, connection);
                command.Parameters.AddWithValue("@DownloadId", downloadId);
                command.Parameters.AddWithValue("@Tag", tag);
                command.ExecuteNonQuery();
            }
        }

        private List<string> GetTags(int downloadId)
        {
            var tags = new List<string>();

            using var connection = new SQLiteConnection(_connectionString);
            connection.Open();

            var query = "SELECT Tag FROM Tags WHERE DownloadId = @DownloadId";
            using var command = new SQLiteCommand(query, connection);
            command.Parameters.AddWithValue("@DownloadId", downloadId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tags.Add(reader.GetString(0));
            }

            return tags;
        }
    }
}

