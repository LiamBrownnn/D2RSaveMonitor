using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace D2RSaveMonitor
{
    /// <summary>
    /// 파일 변경 이벤트 인자 / File change event args
    /// </summary>
    public class FileChangedEventArgs : EventArgs
    {
        public List<string> ChangedPaths { get; set; }
    }

    /// <summary>
    /// 모니터링 상태 변경 이벤트 인자 / Monitoring status change event args
    /// </summary>
    public class MonitoringStatusEventArgs : EventArgs
    {
        public bool IsMonitoring { get; set; }
        public string Message { get; set; }
        public bool IsError { get; set; }
    }

    /// <summary>
    /// 파일 모니터링 서비스 / File monitoring service
    /// Separates file watching concerns from UI layer
    /// </summary>
    public class FileMonitoringService : IDisposable
    {
        #region Fields
        private FileSystemWatcher fileWatcher;
        private System.Windows.Forms.Timer debounceTimer;
        private readonly HashSet<string> pendingChanges = new HashSet<string>();
        private readonly object debounceTimerLock = new object();
        private string monitoredPath;
        private bool isMonitoring;
        private bool disposed = false;
        #endregion

        #region Events
        /// <summary>
        /// 파일 변경 감지 이벤트 (디바운싱 적용) / Files changed event (debounced)
        /// </summary>
        public event EventHandler<FileChangedEventArgs> FilesChanged;

        /// <summary>
        /// 모니터링 오류 이벤트 / Monitoring error event
        /// </summary>
        public event EventHandler<MonitoringStatusEventArgs> MonitoringError;

        /// <summary>
        /// 모니터링 상태 변경 이벤트 / Monitoring status changed event
        /// </summary>
        public event EventHandler<MonitoringStatusEventArgs> StatusChanged;
        #endregion

        #region Properties
        /// <summary>
        /// 현재 모니터링 중인지 여부 / Whether currently monitoring
        /// </summary>
        public bool IsMonitoring => isMonitoring;

        /// <summary>
        /// 모니터링 중인 경로 / Monitored path
        /// </summary>
        public string MonitoredPath => monitoredPath;
        #endregion

        #region Public Methods
        /// <summary>
        /// 모니터링 시작 / Start monitoring
        /// </summary>
        public void StartMonitoring(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                RaiseMonitoringError("모니터링 경로가 지정되지 않았습니다");
                return;
            }

            if (!Directory.Exists(path))
            {
                RaiseMonitoringError($"경로를 찾을 수 없습니다: {path}");
                return;
            }

            try
            {
                // 기존 모니터링 정지
                StopMonitoring();

                monitoredPath = path;

                // Initialize FileSystemWatcher
                fileWatcher = new FileSystemWatcher
                {
                    Path = path,
                    Filter = "*.d2s",
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };

                // Subscribe to events
                fileWatcher.Changed += OnFileChanged;
                fileWatcher.Created += OnFileChanged;
                fileWatcher.Deleted += OnFileChanged;
                fileWatcher.Renamed += OnFileRenamed;
                fileWatcher.Error += OnWatcherError;

                // Initialize debounce timer
                debounceTimer = new System.Windows.Forms.Timer
                {
                    Interval = TimingConstants.FileChangeDebounceMs
                };
                debounceTimer.Tick += DebounceTimer_Tick;

                isMonitoring = true;

                RaiseStatusChanged($"모니터링 시작: {path}", false);
            }
            catch (Exception ex)
            {
                isMonitoring = false;
                RaiseMonitoringError($"모니터링 시작 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 모니터링 중지 / Stop monitoring
        /// </summary>
        public void StopMonitoring()
        {
            try
            {
                if (debounceTimer != null)
                {
                    debounceTimer.Stop();
                    debounceTimer.Tick -= DebounceTimer_Tick;
                    debounceTimer.Dispose();
                    debounceTimer = null;
                }

                if (fileWatcher != null)
                {
                    fileWatcher.Changed -= OnFileChanged;
                    fileWatcher.Created -= OnFileChanged;
                    fileWatcher.Deleted -= OnFileChanged;
                    fileWatcher.Renamed -= OnFileRenamed;
                    fileWatcher.Error -= OnWatcherError;

                    fileWatcher.EnableRaisingEvents = false;
                    fileWatcher.Dispose();
                    fileWatcher = null;
                }

                lock (debounceTimerLock)
                {
                    pendingChanges.Clear();
                }

                isMonitoring = false;

                RaiseStatusChanged("모니터링 중지됨", false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Monitoring cleanup error: {ex.Message}");
            }
        }
        #endregion

        #region Private Methods - Event Handlers
        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            lock (debounceTimerLock)
            {
                pendingChanges.Add(e.FullPath);

                debounceTimer.Stop();
                debounceTimer.Start();
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            lock (debounceTimerLock)
            {
                pendingChanges.Add(e.OldFullPath);
                pendingChanges.Add(e.FullPath);

                debounceTimer.Stop();
                debounceTimer.Start();
            }
        }

        private void DebounceTimer_Tick(object sender, EventArgs e)
        {
            debounceTimer.Stop();

            lock (debounceTimerLock)
            {
                if (pendingChanges.Count > 0)
                {
                    var changedFiles = new List<string>(pendingChanges);
                    pendingChanges.Clear();

                    // Fire event
                    FilesChanged?.Invoke(this, new FileChangedEventArgs
                    {
                        ChangedPaths = changedFiles
                    });
                }
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            Exception ex = e.GetException();
            HandleWatcherError(ex);
        }

        private async void HandleWatcherError(Exception ex)
        {
            RaiseMonitoringError($"모니터링 오류: {ex?.Message ?? "Unknown error"}");

            // 자동 재시작 시도 / Attempt automatic restart
            try
            {
                string pathToRestart = monitoredPath;
                fileWatcher?.Dispose();

                await Task.Delay(1000);

                if (!string.IsNullOrEmpty(pathToRestart))
                {
                    StartMonitoring(pathToRestart);
                }
            }
            catch (Exception restartEx)
            {
                RaiseMonitoringError($"파일 모니터링 재시작 실패: {restartEx.Message}");
            }
        }
        #endregion

        #region Private Methods - Event Raising
        private void RaiseMonitoringError(string message)
        {
            MonitoringError?.Invoke(this, new MonitoringStatusEventArgs
            {
                IsMonitoring = false,
                Message = message,
                IsError = true
            });
        }

        private void RaiseStatusChanged(string message, bool isError)
        {
            StatusChanged?.Invoke(this, new MonitoringStatusEventArgs
            {
                IsMonitoring = isMonitoring,
                Message = message,
                IsError = isError
            });
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
                    StopMonitoring();
                }
                disposed = true;
            }
        }
        #endregion
    }
}
