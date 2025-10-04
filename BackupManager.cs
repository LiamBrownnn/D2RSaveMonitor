using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace D2RSaveMonitor
{
    /// <summary>
    /// Manages backup and restore operations for save files
    /// </summary>
    public class BackupManager : IDisposable
    {
        #region Fields
        private readonly string backupDirectory;
        private readonly BackupSettings settings;
        private readonly SemaphoreSlim backupLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, DateTime> lastBackupTimes = new ConcurrentDictionary<string, DateTime>();
        private readonly object indexLock = new object();
        private bool disposed = false;
        #endregion

        #region Properties
        public string BackupDirectory => backupDirectory;
        #endregion

        #region Events
        public event EventHandler<BackupEventArgs> BackupStarted;
        public event EventHandler<BackupEventArgs> BackupCompleted;
        public event EventHandler<BackupErrorEventArgs> BackupFailed;
        public event EventHandler<BackupProgressEventArgs> BackupProgress;
        #endregion

        #region Constructor
        public BackupManager(string saveDirectory, BackupSettings settings)
        {
            this.settings = settings ?? BackupSettings.LoadFromRegistry();

            // Determine backup directory
            if (!string.IsNullOrEmpty(settings?.CustomBackupPath))
            {
                backupDirectory = settings.CustomBackupPath;
            }
            else
            {
                backupDirectory = Path.Combine(saveDirectory, "Backups");
            }

            // Ensure backup directory exists
            try
            {
                if (!Directory.Exists(backupDirectory))
                {
                    Directory.CreateDirectory(backupDirectory);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create backup directory: {backupDirectory}", ex);
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Check if backup can be created (cooldown check)
        /// </summary>
        public bool CanCreateBackup(string sourceFile, BackupTrigger trigger)
        {
            // Manual backups always allowed
            if (trigger == BackupTrigger.ManualSingle || trigger == BackupTrigger.ManualBulk)
            {
                return true;
            }

            // Check cooldown for automatic backups
            if (lastBackupTimes.TryGetValue(sourceFile, out DateTime lastBackup))
            {
                var cooldown = TimeSpan.FromSeconds(settings.BackupCooldownSeconds);
                if (DateTime.Now - lastBackup < cooldown)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Create a backup of a single file
        /// </summary>
        public async Task<BackupResult> CreateBackupAsync(string sourceFile, BackupTrigger trigger)
        {
            var startTime = DateTime.Now;

            await backupLock.WaitAsync();
            try
            {
                // Fire started event
                BackupStarted?.Invoke(this, new BackupEventArgs
                {
                    FileName = Path.GetFileName(sourceFile),
                    Trigger = trigger
                });

                // Perform backup
                var result = await PerformBackupAsync(sourceFile, trigger);
                result.Duration = DateTime.Now - startTime;

                if (result.Success)
                {
                    // Update last backup time
                    lastBackupTimes[sourceFile] = DateTime.Now;

                    // Fire completed event
                    BackupCompleted?.Invoke(this, new BackupEventArgs
                    {
                        FileName = Path.GetFileName(sourceFile),
                        Trigger = trigger
                    });

                    // Cleanup old backups
                    await CleanupOldBackupsAsync(Path.GetFileName(sourceFile));
                }
                else
                {
                    // Fire failed event
                    BackupFailed?.Invoke(this, new BackupErrorEventArgs
                    {
                        FileName = Path.GetFileName(sourceFile),
                        ErrorMessage = result.ErrorMessage
                    });
                }

                return result;
            }
            finally
            {
                backupLock.Release();
            }
        }

        /// <summary>
        /// Create backups for multiple files
        /// </summary>
        public async Task<List<BackupResult>> CreateBulkBackupAsync(IEnumerable<string> sourceFiles, BackupTrigger trigger)
        {
            var fileList = sourceFiles.ToList();
            var results = new List<BackupResult>();
            int current = 0;

            foreach (var file in fileList)
            {
                current++;

                // Fire progress event
                BackupProgress?.Invoke(this, new BackupProgressEventArgs
                {
                    Current = current,
                    Total = fileList.Count,
                    CurrentFile = Path.GetFileName(file)
                });

                // Create backup
                var result = await CreateBackupAsync(file, trigger);
                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// Get backup history for a specific character file
        /// </summary>
        public List<BackupMetadata> GetBackupHistory(string characterFile)
        {
            try
            {
                var fileName = Path.GetFileName(characterFile);
                var backups = Directory.GetFiles(backupDirectory, $"{fileName}_*.d2s*")
                    .Select(backupFile => ParseBackupMetadata(backupFile))
                    .Where(metadata => metadata != null &&
                           string.Equals(metadata.OriginalFile, fileName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(m => m.Timestamp)
                    .ToList();

                return backups;
            }
            catch
            {
                return new List<BackupMetadata>();
            }
        }

        /// <summary>
        /// Get all backups
        /// </summary>
        public List<BackupMetadata> GetAllBackups()
        {
            try
            {
                var backups = Directory.GetFiles(backupDirectory, "*.d2s*")
                    .Select(ParseBackupMetadata)
                    .Where(metadata => metadata != null)
                    .OrderByDescending(m => m.Timestamp)
                    .ToList();

                return backups;
            }
            catch
            {
                return new List<BackupMetadata>();
            }
        }

        /// <summary>
        /// Restore from backup
        /// </summary>
        public async Task<RestoreResult> RestoreBackupAsync(BackupMetadata backup, string targetPath, bool createPreRestoreBackup = true)
        {
            var result = new RestoreResult
            {
                RestoredFile = targetPath
            };

            await backupLock.WaitAsync();
            try
            {
                // Check if backup file exists
                var backupPath = Path.Combine(backupDirectory, backup.BackupFile);
                if (!File.Exists(backupPath))
                {
                    result.Success = false;
                    result.ErrorMessage = "Backup file not found";
                    return result;
                }

                // Create pre-restore backup
                if (createPreRestoreBackup && File.Exists(targetPath))
                {
                    var preRestoreResult = await PerformBackupAsync(targetPath, BackupTrigger.PreRestore);
                    if (preRestoreResult.Success)
                    {
                        result.PreRestoreBackup = preRestoreResult.Metadata;
                    }
                }

                // Restore: 압축 파일이면 압축 해제, 아니면 직접 복사
                bool restored;
                if (backup.IsCompressed || backup.BackupFile.EndsWith(".d2s.zip", StringComparison.OrdinalIgnoreCase))
                {
                    restored = await ExtractCompressedBackupAsync(backupPath, targetPath, CancellationToken.None);
                }
                else
                {
                    restored = await CopyFileWithRetryAsync(backupPath, targetPath, CancellationToken.None);
                }

                result.Success = restored;
                if (!restored)
                {
                    result.ErrorMessage = "Failed to restore file";
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
            finally
            {
                backupLock.Release();
            }
        }

        private async Task<bool> ExtractCompressedBackupAsync(string zipPath, string targetPath, CancellationToken ct)
        {
            int maxRetries = 3;
            int delayMs = 100;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    // ZIP 파일에서 첫 번째 엔트리 추출
                    using (FileStream zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                    {
                        var entry = archive.Entries.FirstOrDefault();
                        if (entry == null)
                        {
                            return false; // ZIP 파일에 엔트리가 없음
                        }

                        // 대상 파일로 압축 해제
                        using (Stream entryStream = entry.Open())
                        using (FileStream targetStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await entryStream.CopyToAsync(targetStream, 81920, ct);
                        }
                    }

                    return true;
                }
                catch (IOException ex) when (IsFileLocked(ex) && i < maxRetries - 1)
                {
                    // Exponential backoff
                    await Task.Delay(delayMs * (int)Math.Pow(2, i), ct);
                }
                catch
                {
                    if (i == maxRetries - 1) return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Delete a backup file
        /// </summary>
        public async Task<bool> DeleteBackupAsync(BackupMetadata backup)
        {
            await backupLock.WaitAsync();
            try
            {
                var backupPath = Path.Combine(backupDirectory, backup.BackupFile);
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                backupLock.Release();
            }
        }
        #endregion

        #region Private Methods
        private async Task<BackupResult> PerformBackupAsync(string sourceFile, BackupTrigger trigger)
        {
            var result = new BackupResult();

            try
            {
                // Check source file exists
                if (!File.Exists(sourceFile))
                {
                    result.Success = false;
                    result.ErrorMessage = "Source file not found";
                    return result;
                }

                // Get file info
                var fileInfo = new FileInfo(sourceFile);
                var fileName = fileInfo.Name;
                long originalSize = fileInfo.Length;

                // Generate backup filename
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                bool useCompression = settings.EnableCompression;

                string backupFileName;
                string backupPath;
                bool success;
                long backupFileSize;

                if (useCompression)
                {
                    // 압축 백업: .d2s.zip 확장자 사용
                    backupFileName = $"{fileName}_{timestamp}.d2s.zip";
                    backupPath = Path.Combine(backupDirectory, backupFileName);
                    success = await CreateCompressedBackupAsync(sourceFile, backupPath, CancellationToken.None);
                }
                else
                {
                    // 비압축 백업: .d2s 확장자 사용
                    backupFileName = $"{fileName}_{timestamp}.d2s";
                    backupPath = Path.Combine(backupDirectory, backupFileName);
                    success = await CopyFileWithRetryAsync(sourceFile, backupPath, CancellationToken.None);
                }

                if (success)
                {
                    // 백업 파일 크기 확인
                    var backupFileInfo = new FileInfo(backupPath);
                    backupFileSize = backupFileInfo.Length;

                    // Create metadata
                    result.Metadata = new BackupMetadata
                    {
                        OriginalFile = fileName,
                        BackupFile = backupFileName,
                        Timestamp = DateTime.Now,
                        FileSize = originalSize,  // 원본 파일 크기
                        TriggerReason = trigger,
                        IsAutomatic = trigger != BackupTrigger.ManualSingle && trigger != BackupTrigger.ManualBulk,
                        Status = BackupStatus.Valid,
                        IsCompressed = useCompression,
                        CompressedSize = useCompression ? backupFileSize : originalSize,
                        OriginalSize = originalSize
                    };

                    result.Success = true;
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = useCompression ? "Failed to compress file" : "Failed to copy file";
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private async Task<bool> CreateCompressedBackupAsync(string sourceFile, string zipPath, CancellationToken ct)
        {
            int maxRetries = 3;
            int delayMs = 100;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    // ZIP 파일 생성 및 원본 파일 압축
                    using (FileStream zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                    {
                        string entryName = Path.GetFileName(sourceFile);
                        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

                        using (Stream entryStream = entry.Open())
                        using (FileStream sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            await sourceStream.CopyToAsync(entryStream, 81920, ct);
                        }
                    }

                    return true;
                }
                catch (IOException ex) when (IsFileLocked(ex) && i < maxRetries - 1)
                {
                    // Exponential backoff
                    await Task.Delay(delayMs * (int)Math.Pow(2, i), ct);
                }
                catch
                {
                    // 실패 시 생성된 파일 삭제
                    if (File.Exists(zipPath))
                    {
                        try { File.Delete(zipPath); } catch { }
                    }

                    if (i == maxRetries - 1) return false;
                }
            }

            return false;
        }

        private async Task<bool> CopyFileWithRetryAsync(string source, string destination, CancellationToken ct)
        {
            int maxRetries = 3;
            int delayMs = 100;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using (var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var destStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await sourceStream.CopyToAsync(destStream, 81920, ct);
                        return true;
                    }
                }
                catch (IOException ex) when (IsFileLocked(ex) && i < maxRetries - 1)
                {
                    // Exponential backoff
                    await Task.Delay(delayMs * (int)Math.Pow(2, i), ct);
                }
                catch
                {
                    if (i == maxRetries - 1) return false;
                }
            }

            return false;
        }

        private bool IsFileLocked(IOException ex)
        {
            int errorCode = System.Runtime.InteropServices.Marshal.GetHRForException(ex) & 0xFFFF;
            return errorCode == 32 || errorCode == 33; // ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION
        }

        private async Task CleanupOldBackupsAsync(string characterFile)
        {
            try
            {
                var backups = GetBackupHistory(characterFile);

                if (backups.Count > settings.MaxBackupsPerFile)
                {
                    var toDelete = backups.Skip(settings.MaxBackupsPerFile).ToList();

                    foreach (var backup in toDelete)
                    {
                        await DeleteBackupAsync(backup);
                    }
                }
            }
            catch
            {
                // Cleanup failure is non-critical
            }
        }

        private BackupMetadata ParseBackupMetadata(string backupFilePath)
        {
            try
            {
                var fileInfo = new FileInfo(backupFilePath);
                var fileName = fileInfo.Name;

                // 압축 여부 확인
                bool isCompressed = fileName.EndsWith(".d2s.zip", StringComparison.OrdinalIgnoreCase);
                bool isUncompressed = fileName.EndsWith(".d2s", StringComparison.OrdinalIgnoreCase);

                if (!isCompressed && !isUncompressed)
                {
                    return null;
                }

                string coreName = isCompressed
                    ? fileName.Substring(0, fileName.Length - 4) // ".zip" 제거
                    : fileName;

                if (!coreName.EndsWith(".d2s", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string nameWithoutExt = coreName.Substring(0, coreName.Length - 4);

                int lastUnderscoreIndex = nameWithoutExt.LastIndexOf('_');
                if (lastUnderscoreIndex < 0) return null;

                string timePart = nameWithoutExt.Substring(lastUnderscoreIndex + 1);
                string remaining = nameWithoutExt.Substring(0, lastUnderscoreIndex);

                int secondLastUnderscoreIndex = remaining.LastIndexOf('_');
                if (secondLastUnderscoreIndex < 0) return null;

                string datePart = remaining.Substring(secondLastUnderscoreIndex + 1);
                string baseName = remaining.Substring(0, secondLastUnderscoreIndex) + ".d2s";

                if (datePart.Length != 8) return null;
                if (timePart.Length != 6 && timePart.Length != 9) return null;

                var year = int.Parse(datePart.Substring(0, 4));
                var month = int.Parse(datePart.Substring(4, 2));
                var day = int.Parse(datePart.Substring(6, 2));
                var hour = int.Parse(timePart.Substring(0, 2));
                var minute = int.Parse(timePart.Substring(2, 2));
                var second = int.Parse(timePart.Substring(4, 2));
                int millisecond = timePart.Length > 6 ? int.Parse(timePart.Substring(6)) : 0;

                var timestamp = new DateTime(year, month, day, hour, minute, second, millisecond);

                long compressedSize = fileInfo.Length;
                long originalSize = compressedSize;

                if (isCompressed)
                {
                    try
                    {
                        using (FileStream fs = new FileStream(backupFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
                        {
                            var entry = archive.Entries.FirstOrDefault();
                            if (entry != null)
                            {
                                originalSize = entry.Length;
                            }
                        }
                    }
                    catch
                    {
                        originalSize = compressedSize;
                    }
                }

                return new BackupMetadata
                {
                    OriginalFile = baseName,
                    BackupFile = fileName,
                    Timestamp = timestamp,
                    FileSize = originalSize,
                    Status = BackupStatus.Valid,
                    IsCompressed = isCompressed,
                    CompressedSize = compressedSize,
                    OriginalSize = originalSize
                };
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region IDisposable
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    backupLock?.Dispose();
                }
                disposed = true;
            }
        }
        #endregion
    }
}
