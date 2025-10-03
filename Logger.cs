using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace D2RSaveMonitor
{
    /// <summary>
    /// 로그 레벨 / Log level
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }

    /// <summary>
    /// 간단한 파일 기반 로거 / Simple file-based logger
    /// Thread-safe, async, with configurable log levels
    /// </summary>
    public class Logger : IDisposable
    {
        private static Logger _instance;
        private static readonly object _lock = new object();

        private readonly string _logDirectory;
        private readonly LogLevel _minLevel;
        private readonly ConcurrentQueue<LogEntry> _logQueue;
        private readonly CancellationTokenSource _cts;
        private readonly Task _writerTask;
        private bool _disposed = false;

        private Logger(string logDirectory, LogLevel minLevel = LogLevel.Info)
        {
            _logDirectory = logDirectory;
            _minLevel = minLevel;
            _logQueue = new ConcurrentQueue<LogEntry>();
            _cts = new CancellationTokenSource();

            // 로그 디렉토리 생성
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            // 백그라운드 로그 작성 태스크 시작
            _writerTask = Task.Run(() => ProcessLogQueue(_cts.Token));
        }

        /// <summary>
        /// 싱글톤 인스턴스 가져오기 / Get singleton instance
        /// </summary>
        public static Logger Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            string logDir = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "D2RSaveMonitor",
                                "Logs"
                            );
                            _instance = new Logger(logDir, LogLevel.Info);
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// 로거 초기화 (선택적) / Initialize logger (optional)
        /// </summary>
        public static void Initialize(string logDirectory, LogLevel minLevel = LogLevel.Info)
        {
            lock (_lock)
            {
                _instance?.Dispose();
                _instance = new Logger(logDirectory, minLevel);
            }
        }

        #region Public Logging Methods
        /// <summary>
        /// Debug 레벨 로그 / Debug level log
        /// </summary>
        public static void Debug(string message, Exception ex = null)
        {
            Instance.Log(LogLevel.Debug, message, ex);
        }

        /// <summary>
        /// Info 레벨 로그 / Info level log
        /// </summary>
        public static void Info(string message, Exception ex = null)
        {
            Instance.Log(LogLevel.Info, message, ex);
        }

        /// <summary>
        /// Warning 레벨 로그 / Warning level log
        /// </summary>
        public static void Warning(string message, Exception ex = null)
        {
            Instance.Log(LogLevel.Warning, message, ex);
        }

        /// <summary>
        /// Error 레벨 로그 / Error level log
        /// </summary>
        public static void Error(string message, Exception ex = null)
        {
            Instance.Log(LogLevel.Error, message, ex);
        }

        /// <summary>
        /// Critical 레벨 로그 / Critical level log
        /// </summary>
        public static void Critical(string message, Exception ex = null)
        {
            Instance.Log(LogLevel.Critical, message, ex);
        }
        #endregion

        #region Private Methods
        private void Log(LogLevel level, string message, Exception ex)
        {
            if (level < _minLevel || _disposed)
            {
                return;
            }

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Exception = ex,
                ThreadId = Thread.CurrentThread.ManagedThreadId
            };

            _logQueue.Enqueue(entry);
        }

        private async Task ProcessLogQueue(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 큐에 로그가 있으면 처리
                    if (_logQueue.TryDequeue(out LogEntry entry))
                    {
                        await WriteLogEntry(entry);
                    }
                    else
                    {
                        // 큐가 비어있으면 대기
                        await Task.Delay(100, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 로깅 실패 시 콘솔에 출력 (최후의 수단)
                    System.Diagnostics.Debug.WriteLine($"Logger error: {ex.Message}");
                }
            }

            // 종료 시 남은 로그 모두 처리
            await FlushRemainingLogs();
        }

        private async Task WriteLogEntry(LogEntry entry)
        {
            try
            {
                string logFile = GetLogFilePath();
                string logLine = FormatLogEntry(entry);

                // 비동기로 파일에 추가
                using (var writer = new StreamWriter(logFile, true, Encoding.UTF8))
                {
                    await writer.WriteLineAsync(logLine);
                }

                // 오래된 로그 파일 정리 (일주일 이상)
                CleanupOldLogs();
            }
            catch
            {
                // 로깅 실패는 무시 (무한 루프 방지)
            }
        }

        private string GetLogFilePath()
        {
            string fileName = $"app_{DateTime.Now:yyyyMMdd}.log";
            return Path.Combine(_logDirectory, fileName);
        }

        private string FormatLogEntry(LogEntry entry)
        {
            var sb = new StringBuilder();

            // 타임스탬프 및 레벨
            sb.Append($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} ");
            sb.Append($"[{entry.Level.ToString().ToUpper()}] ");
            sb.Append($"[T{entry.ThreadId}] ");

            // 메시지
            sb.Append(entry.Message);

            // 예외 정보
            if (entry.Exception != null)
            {
                sb.AppendLine();
                sb.Append($"  Exception: {entry.Exception.GetType().Name}");
                sb.AppendLine();
                sb.Append($"  Message: {entry.Exception.Message}");
                sb.AppendLine();
                sb.Append($"  StackTrace: {entry.Exception.StackTrace}");

                // Inner exception
                if (entry.Exception.InnerException != null)
                {
                    sb.AppendLine();
                    sb.Append($"  InnerException: {entry.Exception.InnerException.Message}");
                }
            }

            return sb.ToString();
        }

        private void CleanupOldLogs()
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-7);
                var files = Directory.GetFiles(_logDirectory, "app_*.log");

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoffDate)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
                // 정리 실패는 무시
            }
        }

        private async Task FlushRemainingLogs()
        {
            while (_logQueue.TryDequeue(out LogEntry entry))
            {
                await WriteLogEntry(entry);
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
            if (!_disposed)
            {
                if (disposing)
                {
                    // 종료 신호 보내기
                    _cts.Cancel();

                    // 작성 태스크 완료 대기 (최대 5초)
                    try
                    {
                        _writerTask.Wait(TimeSpan.FromSeconds(5));
                    }
                    catch
                    {
                        // Timeout or error - ignore
                    }

                    _cts.Dispose();
                }
                _disposed = true;
            }
        }
        #endregion

        #region LogEntry Class
        private class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public LogLevel Level { get; set; }
            public string Message { get; set; }
            public Exception Exception { get; set; }
            public int ThreadId { get; set; }
        }
        #endregion
    }
}
