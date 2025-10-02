using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
                var backups = Directory.GetFiles(backupDirectory, $"{fileName}_*.d2s")
                    .Select(backupFile => ParseBackupMetadata(backupFile, fileName))
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
        /// Get all backups
        /// </summary>
        public List<BackupMetadata> GetAllBackups()
        {
            try
            {
                var backups = Directory.GetFiles(backupDirectory, "*.d2s")
                    .Select(backupFile =>
                    {
                        var fileName = Path.GetFileName(backupFile);
                        var underscoreIndex = fileName.LastIndexOf('_');
                        if (underscoreIndex < 0) return null;

                        var originalName = fileName.Substring(0, underscoreIndex) + ".d2s";
                        return ParseBackupMetadata(backupFile, originalName);
                    })
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

                // Copy backup to target location
                bool copied = await CopyFileWithRetryAsync(backupPath, targetPath, CancellationToken.None);

                result.Success = copied;
                if (!copied)
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

                // Generate backup filename
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFileName = $"{fileName}_{timestamp}.d2s";
                var backupPath = Path.Combine(backupDirectory, backupFileName);

                // Copy file
                bool copied = await CopyFileWithRetryAsync(sourceFile, backupPath, CancellationToken.None);

                if (copied)
                {
                    // Create metadata
                    result.Metadata = new BackupMetadata
                    {
                        OriginalFile = fileName,
                        BackupFile = backupFileName,
                        Timestamp = DateTime.Now,
                        FileSize = fileInfo.Length,
                        TriggerReason = trigger,
                        IsAutomatic = trigger != BackupTrigger.ManualSingle && trigger != BackupTrigger.ManualBulk,
                        Status = BackupStatus.Valid
                    };

                    result.Success = true;
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "Failed to copy file";
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

        private BackupMetadata ParseBackupMetadata(string backupFilePath, string originalFileName)
        {
            try
            {
                var fileInfo = new FileInfo(backupFilePath);
                var fileName = fileInfo.Name;

                // Parse timestamp from filename: {name}_{yyyyMMdd_HHmmss}.d2s
                // 예: Amazon.d2s_20251002_082801.d2s

                // 1. .d2s 확장자 제거
                if (!fileName.EndsWith(".d2s", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var fileNameWithoutExt = fileName.Substring(0, fileName.Length - 4); // "Amazon.d2s_20251002_082801"

                // 2. 마지막 언더스코어 찾기 (시간 부분 분리)
                var lastUnderscoreIndex = fileNameWithoutExt.LastIndexOf('_');
                if (lastUnderscoreIndex < 0) return null;

                var timePart = fileNameWithoutExt.Substring(lastUnderscoreIndex + 1); // "082801"
                var remaining = fileNameWithoutExt.Substring(0, lastUnderscoreIndex); // "Amazon.d2s_20251002"

                // 3. 그 다음 언더스코어 찾기 (날짜 부분 분리)
                var secondLastUnderscoreIndex = remaining.LastIndexOf('_');
                if (secondLastUnderscoreIndex < 0) return null;

                var datePart = remaining.Substring(secondLastUnderscoreIndex + 1); // "20251002"

                // 4. 타임스탬프 파싱
                if (datePart.Length != 8 || timePart.Length != 6) return null;

                var year = int.Parse(datePart.Substring(0, 4));
                var month = int.Parse(datePart.Substring(4, 2));
                var day = int.Parse(datePart.Substring(6, 2));
                var hour = int.Parse(timePart.Substring(0, 2));
                var minute = int.Parse(timePart.Substring(2, 2));
                var second = int.Parse(timePart.Substring(4, 2));

                var timestamp = new DateTime(year, month, day, hour, minute, second);

                return new BackupMetadata
                {
                    OriginalFile = originalFileName,
                    BackupFile = fileName,
                    Timestamp = timestamp,
                    FileSize = fileInfo.Length,
                    Status = BackupStatus.Valid
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
