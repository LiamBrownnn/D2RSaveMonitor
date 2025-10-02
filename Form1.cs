using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace D2RSaveMonitor
{
    #region Constants
    /// <summary>
    /// File monitoring and size limit constants for D2R save files
    /// </summary>
    public static class FileConstants
    {
        /// <summary>
        /// Maximum allowed save file size in bytes (D2R limit)
        /// </summary>
        public const long MaxFileSize = 8192;

        /// <summary>
        /// Danger threshold - file approaching critical size (91.5% of limit)
        /// </summary>
        public const long DangerThreshold = 7500;

        /// <summary>
        /// Warning threshold - file size requires attention (85.4% of limit)
        /// </summary>
        public const long WarningThreshold = 7000;

        /// <summary>
        /// Danger threshold as percentage (7500/8192 * 100 = 91.5%)
        /// </summary>
        public const double DangerPercentage = 91.5;

        /// <summary>
        /// Warning threshold as percentage (7000/8192 * 100 = 85.4%)
        /// </summary>
        public const double WarningPercentage = 85.4;
    }

    /// <summary>
    /// Timing configuration for monitoring and UI updates
    /// </summary>
    public static class TimingConstants
    {
        /// <summary>
        /// FileSystemWatcher debounce delay to prevent rapid re-triggers
        /// </summary>
        public const int FileChangeDebounceMs = 500;

        /// <summary>
        /// Overlay warning auto-close duration in milliseconds
        /// </summary>
        public const int OverlayDisplayDurationMs = 2000;

        /// <summary>
        /// Retry delay for locked file access attempts
        /// </summary>
        public const int FileAccessRetryDelayMs = 100;

        /// <summary>
        /// Maximum retry attempts for locked files
        /// </summary>
        public const int MaxFileAccessRetries = 3;
    }

    /// <summary>
    /// UI layout and positioning constants
    /// </summary>
    public static class UIConstants
    {
        // Overlay Form Dimensions
        public const int OverlayWidth = 320;
        public const int OverlayHeight = 110;

        // Overlay Positioning
        public const int OverlayMarginRight = 20;
        public const int OverlayMarginTop = 20;

        // Progress Bar Colors
        public static readonly Color SafeColor = Color.LightGreen;
        public static readonly Color WarningColor = Color.LightYellow;
        public static readonly Color DangerColor = Color.LightCoral;

        public static readonly Color ProgressBarSafe = Color.Green;
        public static readonly Color ProgressBarWarning = Color.Orange;
        public static readonly Color ProgressBarDanger = Color.Red;

        // DataGridView Row Height
        public const int DataGridRowHeight = 30;
    }

    /// <summary>
    /// Registry and configuration persistence
    /// </summary>
    public static class ConfigConstants
    {
        public const string RegistryKeyPath = @"Software\D2RMonitor";
        public const string SavePathValueName = "SavePath";
        public const string OverlayEnabledValueName = "OverlayEnabled";
    }
    #endregion

    public partial class Form1 : Form
    {
        #region Fields
        private string savePath = "";
        private FileSystemWatcher fileWatcher;
        private System.Windows.Forms.Timer debounceTimer;
        private readonly HashSet<string> pendingChanges = new HashSet<string>();
        private readonly object debounceTimerLock = new object();
        private OverlayManager overlayManager;
        private bool overlayShown = false;
        private bool overlayEnabled = true;

        // Backup System
        private BackupManager backupManager;
        private System.Threading.Timer periodicBackupTimer;
        private BackupSettings backupSettings;

#if DEBUG
        private bool debugMode = false;
#endif

        // UI Controls
        private TextBox txtSavePath;
        private Button btnBrowse;
        private Button btnOpenSaveFolder;
        private Button btnOpenBackupFolder;
        private DataGridView dgvFiles;
        private Label lblStatus;

        // Backup UI Controls
        private GroupBox grpBackup;
        private Button btnBackupSelected;
        private Button btnBackupAll;
        private Button btnViewBackups;
        private Button btnBackupSettings;
        private Label lblLastBackup;
        private Label lblBackupStatus;
        private ProgressBar pbBackupProgress;

        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "D2RMonitor",
            "errors.log"
        );
        #endregion

        public Form1()
        {
            InitializeComponent();
            InitializeUI();
            LoadSettings();
            overlayManager = new OverlayManager();
            InitializeBackupSystem();
            StartMonitoring();
        }

        private void InitializeUI()
        {
            // 폼 설정
            Text = "디아블로 2 레저렉션 세이브 모니터";
            Size = new Size(900, 720);  // Increased height for backup panel
            StartPosition = FormStartPosition.CenterScreen;

            // 경로 레이블
            Label lblPath = new Label
            {
                Text = "세이브 파일 경로:",
                Location = new Point(10, 15),
                Size = new Size(120, 20)
            };
            Controls.Add(lblPath);

            // 경로 텍스트박스
            txtSavePath = new TextBox
            {
                Location = new Point(130, 12),
                Size = new Size(400, 25),
                ReadOnly = true
            };
            Controls.Add(txtSavePath);

            // ToolTip 설정
            ToolTip toolTip = new ToolTip();
            toolTip.AutoPopDelay = 5000;
            toolTip.InitialDelay = 500;
            toolTip.ReshowDelay = 100;

            // 세이브 폴더 열기 버튼
            btnOpenSaveFolder = new Button
            {
                Text = "📁 세이브",
                Location = new Point(540, 10),
                Size = new Size(80, 28)
            };
            btnOpenSaveFolder.Click += BtnOpenSaveFolder_Click;
            toolTip.SetToolTip(btnOpenSaveFolder, "세이브 폴더 열기");
            Controls.Add(btnOpenSaveFolder);

            // 백업 폴더 열기 버튼
            btnOpenBackupFolder = new Button
            {
                Text = "📁 백업",
                Location = new Point(625, 10),
                Size = new Size(80, 28)
            };
            btnOpenBackupFolder.Click += BtnOpenBackupFolder_Click;
            toolTip.SetToolTip(btnOpenBackupFolder, "백업 폴더 열기");
            Controls.Add(btnOpenBackupFolder);

            // 찾아보기 버튼
            btnBrowse = new Button
            {
                Text = "찾아보기...",
                Location = new Point(780, 10),
                Size = new Size(100, 28)
            };
            btnBrowse.Click += BtnBrowse_Click;
            Controls.Add(btnBrowse);

#if DEBUG
            // 디버그 모드 토글 버튼 (DEBUG 빌드에서만 표시)
            Button btnDebugMode = new Button
            {
                Text = "디버그: OFF",
                Location = new Point(670, 10),
                Size = new Size(100, 28),
                BackColor = Color.Gray,
                ForeColor = Color.White,
                Font = new Font(Font, FontStyle.Bold)
            };
            btnDebugMode.Click += BtnDebugMode_Click;
            Controls.Add(btnDebugMode);
#endif

            // DataGridView
            dgvFiles = new DataGridView
            {
                Location = new Point(10, 50),
                Size = new Size(870, 420),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,  // Ctrl 키로 여러 파일 선택 가능
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToResizeColumns = false  // 컬럼 크기 조절 금지
            };
            dgvFiles.CellPainting += DgvFiles_CellPainting;
            dgvFiles.SelectionChanged += DgvFiles_SelectionChanged;

            // 컬럼 설정
            dgvFiles.Columns.Add("FileName", "캐릭터 파일명");
            dgvFiles.Columns.Add("CurrentSize", "현재 크기");
            dgvFiles.Columns.Add("Limit", "제한");
            dgvFiles.Columns.Add("Percentage", "사용률");
            dgvFiles.Columns.Add("ProgressBar", "진행 상태");
            dgvFiles.Columns.Add("Status", "상태");

            dgvFiles.Columns["FileName"].Width = 200;
            dgvFiles.Columns["CurrentSize"].Width = 100;
            dgvFiles.Columns["Limit"].Width = 100;
            dgvFiles.Columns["Percentage"].Width = 80;
            dgvFiles.Columns["ProgressBar"].Width = 200;
            dgvFiles.Columns["Status"].Width = 120;

            dgvFiles.RowTemplate.Height = UIConstants.DataGridRowHeight;

            Controls.Add(dgvFiles);

            // 백업 관리 패널
            grpBackup = new GroupBox
            {
                Text = "백업 관리",
                Location = new Point(10, 480),
                Size = new Size(870, 160),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            Controls.Add(grpBackup);

            // 백업 선택 버튼
            btnBackupSelected = new Button
            {
                Text = "선택 파일 백업",
                Location = new Point(10, 25),
                Size = new Size(120, 30),
                Enabled = false
            };
            btnBackupSelected.Click += BtnBackupSelected_Click;
            grpBackup.Controls.Add(btnBackupSelected);

            // 전체 백업 버튼
            btnBackupAll = new Button
            {
                Text = "전체 백업",
                Location = new Point(140, 25),
                Size = new Size(120, 30)
            };
            btnBackupAll.Click += BtnBackupAll_Click;
            grpBackup.Controls.Add(btnBackupAll);

            // 복원 버튼
            btnViewBackups = new Button
            {
                Text = "백업 복원...",
                Location = new Point(270, 25),
                Size = new Size(120, 30)
            };
            btnViewBackups.Click += BtnViewBackups_Click;
            grpBackup.Controls.Add(btnViewBackups);

            // 설정 버튼
            btnBackupSettings = new Button
            {
                Text = "백업 설정...",
                Location = new Point(400, 25),
                Size = new Size(120, 30)
            };
            btnBackupSettings.Click += BtnBackupSettings_Click;
            grpBackup.Controls.Add(btnBackupSettings);

            // 마지막 백업 레이블
            lblLastBackup = new Label
            {
                Text = "마지막 백업: 없음",
                Location = new Point(10, 65),
                AutoSize = true
            };
            grpBackup.Controls.Add(lblLastBackup);

            // 백업 상태 레이블
            lblBackupStatus = new Label
            {
                Text = "자동 백업: 활성화 | 주기적 백업: 비활성화",
                Location = new Point(10, 85),
                AutoSize = true
            };
            grpBackup.Controls.Add(lblBackupStatus);

            // 백업 진행 표시줄
            pbBackupProgress = new ProgressBar
            {
                Location = new Point(10, 110),
                Size = new Size(850, 20),
                Visible = false,
                Style = ProgressBarStyle.Continuous
            };
            grpBackup.Controls.Add(pbBackupProgress);

            // 진행 상태 레이블 (진행바 위)
            Label lblProgress = new Label
            {
                Name = "lblProgressText",
                Text = "",
                Location = new Point(10, 135),
                Size = new Size(850, 15),
                Visible = false
            };
            grpBackup.Controls.Add(lblProgress);

            // 상태 레이블 (패널 아래로 이동)
            lblStatus = new Label
            {
                Text = "모니터링 대기 중...",
                Location = new Point(10, 650),
                Size = new Size(870, 20),
                ForeColor = Color.Gray,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            Controls.Add(lblStatus);
        }

        #region Backup System Methods
        private void InitializeBackupSystem()
        {
            try
            {
                // Load backup settings
                backupSettings = BackupSettings.LoadFromRegistry();

                // Create backup manager
                backupManager = new BackupManager(savePath, backupSettings);

                // Subscribe to backup events
                backupManager.BackupStarted += OnBackupStarted;
                backupManager.BackupCompleted += OnBackupCompleted;
                backupManager.BackupFailed += OnBackupFailed;
                backupManager.BackupProgress += OnBackupProgress;

                // Start periodic backup timer if enabled
                if (backupSettings.PeriodicBackupEnabled)
                {
                    StartPeriodicBackupTimer();
                }

                // Update backup status display
                UpdateBackupStatusDisplay();
            }
            catch (Exception ex)
            {
                LogError("백업 시스템 초기화 실패", ex);
                MessageBox.Show(
                    $"백업 시스템 초기화 실패:\n{ex.Message}\n\n모니터링은 계속됩니다.",
                    "경고",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        private void StartPeriodicBackupTimer()
        {
            if (periodicBackupTimer != null)
            {
                periodicBackupTimer.Dispose();
            }

            int intervalMs = backupSettings.PeriodicIntervalMinutes * 60 * 1000;
            periodicBackupTimer = new System.Threading.Timer(
                async _ => await PerformPeriodicBackup(),
                null,
                intervalMs,
                intervalMs
            );
        }

        private void StopPeriodicBackupTimer()
        {
            if (periodicBackupTimer != null)
            {
                periodicBackupTimer.Dispose();
                periodicBackupTimer = null;
            }
        }

        private async Task PerformPeriodicBackup()
        {
            try
            {
                if (!backupSettings.PeriodicBackupEnabled) return;

                var files = Directory.GetFiles(savePath, "*.d2s")
                    .Where(f => new FileInfo(f).Length >= FileConstants.WarningThreshold)
                    .ToList();

                if (files.Any())
                {
                    await backupManager.CreateBulkBackupAsync(files, BackupTrigger.PeriodicAutomatic);
                }
            }
            catch (Exception ex)
            {
                LogError("주기적 백업 실패", ex);
            }
        }

        private void UpdateBackupStatusDisplay()
        {
            if (lblBackupStatus.InvokeRequired)
            {
                lblBackupStatus.Invoke(new Action(UpdateBackupStatusDisplay));
                return;
            }

            string status = $"자동 백업: {(backupSettings.AutoBackupOnDanger ? "활성화" : "비활성화")}";

            if (backupSettings.PeriodicBackupEnabled)
            {
                status += $" | 주기적 백업: {backupSettings.PeriodicIntervalMinutes}분마다";
            }
            else
            {
                status += " | 주기적 백업: 비활성화";
            }

            lblBackupStatus.Text = status;
        }

        private void OnBackupStarted(object sender, BackupEventArgs e)
        {
            // Event fired on ThreadPool, marshal to UI thread
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnBackupStarted(sender, e)));
                return;
            }

            UpdateStatus($"백업 중: {e.FileName}", Color.Blue);
        }

        private void OnBackupCompleted(object sender, BackupEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnBackupCompleted(sender, e)));
                return;
            }

            lblLastBackup.Text = $"마지막 백업: {DateTime.Now:HH:mm:ss}";
            UpdateStatus($"백업 완료: {e.FileName}", Color.Green);
        }

        private void OnBackupFailed(object sender, BackupErrorEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnBackupFailed(sender, e)));
                return;
            }

            UpdateStatus($"백업 실패: {e.FileName} - {e.ErrorMessage}", Color.Red);
            LogError($"백업 실패: {e.FileName}", e.Exception);
        }

        private void OnBackupProgress(object sender, BackupProgressEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnBackupProgress(sender, e)));
                return;
            }

            pbBackupProgress.Visible = true;
            pbBackupProgress.Maximum = e.Total;
            pbBackupProgress.Value = e.Current;

            var lblProgress = grpBackup.Controls.Find("lblProgressText", false).FirstOrDefault() as Label;
            if (lblProgress != null)
            {
                lblProgress.Visible = true;
                lblProgress.Text = $"백업 중: {e.Current}/{e.Total} - {e.CurrentFile}";
            }

            if (e.Current >= e.Total)
            {
                // Hide progress after brief delay
                Task.Delay(1000).ContinueWith(_ =>
                {
                    if (InvokeRequired)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            pbBackupProgress.Visible = false;
                            if (lblProgress != null) lblProgress.Visible = false;
                        }));
                    }
                });
            }
        }
        #endregion

        #region Monitoring Methods
        private void StartMonitoring()
        {
            if (!ValidateSavePath(savePath))
            {
                return;
            }

            try
            {
                // Initialize FileSystemWatcher
                fileWatcher = new FileSystemWatcher
                {
                    Path = savePath,
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

                // Initial scan
                CheckSaveFiles();

                UpdateStatus($"모니터링 시작: {savePath}", Color.Green);
            }
            catch (Exception ex)
            {
                HandleMonitoringError($"모니터링 시작 실패: {ex.Message}");
            }
        }

        private void StopMonitoring()
        {
            try
            {
                if (debounceTimer != null)
                {
                    debounceTimer.Stop();
                    debounceTimer.Dispose();
                    debounceTimer = null;
                }

                if (fileWatcher != null)
                {
                    fileWatcher.EnableRaisingEvents = false;
                    fileWatcher.Dispose();
                    fileWatcher = null;
                }
            }
            catch (Exception ex)
            {
                LogError("Monitoring cleanup error", ex);
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnFileChanged(sender, e)));
                return;
            }

            lock (debounceTimerLock)
            {
                pendingChanges.Add(e.FullPath);

                debounceTimer.Stop();
                debounceTimer.Start();
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnFileRenamed(sender, e)));
                return;
            }

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
                    pendingChanges.Clear();
                    CheckSaveFiles();
                }
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            Exception ex = e.GetException();

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => HandleWatcherError(ex)));
                return;
            }

            HandleWatcherError(ex);
        }

        private void HandleWatcherError(Exception ex)
        {
            UpdateStatus($"모니터링 오류: {ex?.Message ?? "Unknown error"}", Color.Red);

            try
            {
                fileWatcher?.Dispose();
                System.Threading.Thread.Sleep(1000);
                StartMonitoring();
            }
            catch (Exception restartEx)
            {
                LogError("파일 모니터링 재시작 실패", restartEx);
                MessageBox.Show(
                    $"파일 모니터링 재시작 실패:\n{restartEx.Message}\n\n애플리케이션을 다시 시작하세요.",
                    "심각한 오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private void HandleMonitoringError(string message)
        {
            UpdateStatus(message, Color.Red);
            LogError(message, null);
        }
        #endregion

        #region Settings Methods
        private void LoadSettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ConfigConstants.RegistryKeyPath))
                {
                    if (key != null)
                    {
                        object savedPath = key.GetValue(ConfigConstants.SavePathValueName, GetDefaultSavePath());
                        savePath = savedPath?.ToString() ?? GetDefaultSavePath();

                        object savedOverlay = key.GetValue(ConfigConstants.OverlayEnabledValueName, true);
                        if (savedOverlay is int intValue)
                        {
                            overlayEnabled = intValue != 0;
                        }
                    }
                    else
                    {
                        savePath = GetDefaultSavePath();
                    }
                }

                txtSavePath.Text = savePath;
            }
            catch (UnauthorizedAccessException)
            {
                savePath = GetDefaultSavePath();
                txtSavePath.Text = savePath;
                LogError("레지스트리 접근 거부 - 기본값 사용", null);
            }
            catch (Exception ex)
            {
                savePath = GetDefaultSavePath();
                txtSavePath.Text = savePath;
                LogError("설정 로드 실패", ex);
            }
        }

        private void SaveSettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ConfigConstants.RegistryKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue(ConfigConstants.SavePathValueName, savePath);
                        key.SetValue(ConfigConstants.OverlayEnabledValueName, overlayEnabled ? 1 : 0);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                LogError("레지스트리 쓰기 권한 없음", null);
            }
            catch (Exception ex)
            {
                LogError("설정 저장 실패", ex);
                MessageBox.Show($"설정 저장 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        #region UI Event Handlers
        private string GetDefaultSavePath()
        {
            string username = Environment.UserName;
            return $@"C:\Users\{username}\Saved Games\Diablo II Resurrected\";
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "디아블로 2 세이브 폴더를 선택하세요";
                dialog.SelectedPath = savePath;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    savePath = dialog.SelectedPath;
                    txtSavePath.Text = savePath;
                    SaveSettings();

                    // Restart monitoring and backup system with new path
                    StopMonitoring();

                    // Reinitialize backup manager with new path
                    if (backupManager != null)
                    {
                        backupManager.Dispose();
                        backupManager = new BackupManager(savePath, backupSettings);
                        backupManager.BackupStarted += OnBackupStarted;
                        backupManager.BackupCompleted += OnBackupCompleted;
                        backupManager.BackupFailed += OnBackupFailed;
                        backupManager.BackupProgress += OnBackupProgress;
                    }

                    StartMonitoring();
                }
            }
        }

        private void BtnOpenSaveFolder_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(savePath))
                {
                    MessageBox.Show(
                        "세이브 경로가 설정되지 않았습니다.",
                        "알림",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return;
                }

                if (!Directory.Exists(savePath))
                {
                    MessageBox.Show(
                        $"세이브 폴더를 찾을 수 없습니다:\n{savePath}",
                        "오류",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                // 탐색기로 폴더 열기
                System.Diagnostics.Process.Start("explorer.exe", savePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"폴더를 열 수 없습니다:\n{ex.Message}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                LogError("세이브 폴더 열기 실패", ex);
            }
        }

        private void BtnOpenBackupFolder_Click(object sender, EventArgs e)
        {
            try
            {
                if (backupManager == null)
                {
                    MessageBox.Show(
                        "백업 시스템이 초기화되지 않았습니다.",
                        "알림",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return;
                }

                // BackupManager에서 백업 폴더 경로 가져오기
                string backupDirectory;
                if (!string.IsNullOrEmpty(backupSettings?.CustomBackupPath))
                {
                    backupDirectory = backupSettings.CustomBackupPath;
                }
                else
                {
                    backupDirectory = Path.Combine(savePath, "Backups");
                }

                // 백업 폴더가 없으면 생성
                if (!Directory.Exists(backupDirectory))
                {
                    Directory.CreateDirectory(backupDirectory);
                }

                // 탐색기로 폴더 열기
                System.Diagnostics.Process.Start("explorer.exe", backupDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"백업 폴더를 열 수 없습니다:\n{ex.Message}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                LogError("백업 폴더 열기 실패", ex);
            }
        }

        private void DgvFiles_SelectionChanged(object sender, EventArgs e)
        {
            // Enable/disable backup selected button based on selection
            int selectedCount = dgvFiles.SelectedRows.Count;
            btnBackupSelected.Enabled = selectedCount > 0;

            // 버튼 텍스트 업데이트 (선택된 개수 표시)
            if (selectedCount > 1)
            {
                btnBackupSelected.Text = $"선택 파일 백업 ({selectedCount}개)";
            }
            else
            {
                btnBackupSelected.Text = "선택 파일 백업";
            }
        }

        private async void BtnBackupSelected_Click(object sender, EventArgs e)
        {
            if (dgvFiles.SelectedRows.Count == 0) return;

            try
            {
                btnBackupSelected.Enabled = false;
                btnBackupAll.Enabled = false;

                // 선택된 모든 파일 경로 수집
                var selectedFiles = new List<string>();
                foreach (DataGridViewRow row in dgvFiles.SelectedRows)
                {
                    string fileName = row.Cells["FileName"].Value.ToString();
                    string fullPath = Path.Combine(savePath, fileName);
                    selectedFiles.Add(fullPath);
                }

                // 단일 파일인 경우
                if (selectedFiles.Count == 1)
                {
                    var result = await backupManager.CreateBackupAsync(selectedFiles[0], BackupTrigger.ManualSingle);

                    if (result.Success)
                    {
                        MessageBox.Show(
                            $"백업 완료: {Path.GetFileName(selectedFiles[0])}\n백업 시간: {result.Duration.TotalMilliseconds:F0}ms",
                            "백업 성공",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }
                    else
                    {
                        MessageBox.Show(
                            $"백업 실패: {Path.GetFileName(selectedFiles[0])}\n오류: {result.ErrorMessage}",
                            "백업 실패",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                    }
                }
                // 여러 파일인 경우
                else
                {
                    var confirmResult = MessageBox.Show(
                        $"{selectedFiles.Count}개의 파일을 백업하시겠습니까?",
                        "선택 파일 백업 확인",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    if (confirmResult != DialogResult.Yes) return;

                    // 벌크 백업 실행
                    var results = await backupManager.CreateBulkBackupAsync(selectedFiles, BackupTrigger.ManualBulk);

                    int successCount = results.Count(r => r.Success);
                    int failCount = results.Count(r => !r.Success);

                    string message = $"백업 완료: {successCount}개 성공";
                    if (failCount > 0)
                    {
                        message += $", {failCount}개 실패";
                    }

                    MessageBox.Show(
                        message,
                        "선택 파일 백업 완료",
                        MessageBoxButtons.OK,
                        failCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"백업 중 오류 발생:\n{ex.Message}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                LogError("수동 백업 실패", ex);
            }
            finally
            {
                btnBackupSelected.Enabled = dgvFiles.SelectedRows.Count > 0;
                btnBackupAll.Enabled = true;
            }
        }

        private async void BtnBackupAll_Click(object sender, EventArgs e)
        {
            try
            {
                var files = Directory.GetFiles(savePath, "*.d2s").ToList();

                if (files.Count == 0)
                {
                    MessageBox.Show(
                        "백업할 파일이 없습니다.",
                        "알림",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return;
                }

                var confirmResult = MessageBox.Show(
                    $"{files.Count}개의 캐릭터 파일을 백업하시겠습니까?",
                    "전체 백업 확인",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (confirmResult != DialogResult.Yes) return;

                btnBackupSelected.Enabled = false;
                btnBackupAll.Enabled = false;

                var results = await backupManager.CreateBulkBackupAsync(files, BackupTrigger.ManualBulk);

                int successCount = results.Count(r => r.Success);
                int failCount = results.Count(r => !r.Success);

                string message = $"백업 완료: {successCount}개 성공";
                if (failCount > 0)
                {
                    message += $", {failCount}개 실패";
                }

                MessageBox.Show(
                    message,
                    "전체 백업 완료",
                    MessageBoxButtons.OK,
                    failCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"전체 백업 중 오류 발생:\n{ex.Message}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                LogError("전체 백업 실패", ex);
            }
            finally
            {
                btnBackupSelected.Enabled = dgvFiles.SelectedRows.Count > 0;
                btnBackupAll.Enabled = true;
            }
        }

        private void BtnViewBackups_Click(object sender, EventArgs e)
        {
            try
            {
                using (var backupHistoryForm = new BackupHistoryForm(backupManager, savePath))
                {
                    if (backupHistoryForm.ShowDialog() == DialogResult.OK)
                    {
                        // Refresh file list after restore
                        CheckSaveFiles();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"백업 목록을 열 수 없습니다:\n{ex.Message}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                LogError("백업 목록 열기 실패", ex);
            }
        }

        private void BtnBackupSettings_Click(object sender, EventArgs e)
        {
            try
            {
                using (var settingsForm = new BackupSettingsForm(backupSettings))
                {
                    if (settingsForm.ShowDialog() == DialogResult.OK)
                    {
                        // Settings were saved in the dialog
                        backupSettings = BackupSettings.LoadFromRegistry();

                        // Update periodic backup timer
                        StopPeriodicBackupTimer();
                        if (backupSettings.PeriodicBackupEnabled)
                        {
                            StartPeriodicBackupTimer();
                        }

                        // Update status display
                        UpdateBackupStatusDisplay();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"설정을 열 수 없습니다:\n{ex.Message}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                LogError("백업 설정 열기 실패", ex);
            }
        }

#if DEBUG
        private void BtnDebugMode_Click(object sender, EventArgs e)
        {
            debugMode = !debugMode;
            Button btn = (Button)sender;

            if (debugMode)
            {
                btn.Text = "디버그: ON";
                btn.BackColor = Color.OrangeRed;
                UpdateStatus("디버그 모드 활성화 - 테스트 데이터 표시", Color.Orange);

                // Immediately show test data
                DisplayDebugData();

                // Show test overlay
                overlayManager.ShowWarning("TestCharacter.d2s", 7856);
            }
            else
            {
                btn.Text = "디버그: OFF";
                btn.BackColor = Color.Gray;
                UpdateStatus("디버그 모드 비활성화", Color.Gray);

                // Refresh with real data
                CheckSaveFiles();
            }
        }

        private void DisplayDebugData()
        {
            dgvFiles.Rows.Clear();

            // Generate test scenarios
            var testCases = new[]
            {
                ("SafeCharacter.d2s", 6500L, "안전"),
                ("WarningCharacter.d2s", 7200L, "주의"),
                ("DangerCharacter.d2s", 7856L, "위험"),
                ("CriticalCharacter.d2s", 8100L, "위험")
            };

            foreach (var (fileName, fileSize, expectedStatus) in testCases)
            {
                long limit = FileConstants.MaxFileSize;
                double percentage = (double)fileSize / limit * 100;

                ProcessSaveFileData(fileName, fileSize, limit, percentage);
            }

            UpdateStatus($"디버그 모드: {testCases.Length}개 테스트 케이스", Color.Orange);
        }
#endif
        #endregion

        #region File Processing Methods
        private void CheckSaveFiles()
        {
#if DEBUG
            if (debugMode)
            {
                return; // Don't overwrite debug data
            }
#endif

            if (dgvFiles.InvokeRequired)
            {
                dgvFiles.Invoke(new Action(CheckSaveFiles));
                return;
            }

            try
            {
                dgvFiles.Rows.Clear();

                if (!ValidateSavePath(savePath))
                {
                    return;
                }

                string[] d2sFiles;
                try
                {
                    d2sFiles = Directory.GetFiles(savePath, "*.d2s");
                }
                catch (UnauthorizedAccessException)
                {
                    UpdateStatus("세이브 폴더 접근 권한 없음", Color.Red);
                    return;
                }
                catch (DirectoryNotFoundException)
                {
                    UpdateStatus("세이브 폴더를 찾을 수 없음", Color.Red);
                    return;
                }

                bool anyWarning = false;
                int successCount = 0;
                int errorCount = 0;

                foreach (string filePath in d2sFiles)
                {
                    try
                    {
                        FileInfo fileInfo = GetFileInfoWithRetry(filePath);
                        if (fileInfo == null)
                        {
                            errorCount++;
                            continue;
                        }

                        ProcessSaveFile(fileInfo, ref anyWarning);
                        successCount++;
                    }
                    catch (FileNotFoundException)
                    {
                        // File deleted between enumeration and processing - ignore
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        LogError($"파일 접근 거부: {Path.GetFileName(filePath)}", null);
                        errorCount++;
                    }
                    catch (IOException ex)
                    {
                        LogError($"파일 읽기 실패: {Path.GetFileName(filePath)}", ex);
                        errorCount++;
                    }
                    catch (Exception ex)
                    {
                        LogError($"예상치 못한 오류: {Path.GetFileName(filePath)}", ex);
                        errorCount++;
                    }
                }

                if (!anyWarning)
                {
                    overlayShown = false;
                }

                string statusMessage = $"마지막 체크: {DateTime.Now:HH:mm:ss} - {successCount}개 파일 처리됨";

                if (errorCount > 0)
                {
                    statusMessage += $" ({errorCount}개 오류)";
                }

                UpdateStatus(statusMessage, anyWarning ? Color.Red : Color.Green);
            }
            catch (Exception ex)
            {
                HandleCriticalError("파일 체크 중 심각한 오류", ex);
            }
        }

        private FileInfo GetFileInfoWithRetry(string filePath)
        {
            for (int attempt = 0; attempt < TimingConstants.MaxFileAccessRetries; attempt++)
            {
                try
                {
                    FileInfo fileInfo = new FileInfo(filePath);
                    long size = fileInfo.Length; // Force read to test lock
                    return fileInfo;
                }
                catch (IOException) when (attempt < TimingConstants.MaxFileAccessRetries - 1)
                {
                    // File locked, wait and retry
                    System.Threading.Thread.Sleep(TimingConstants.FileAccessRetryDelayMs);
                }
                catch (UnauthorizedAccessException)
                {
                    // Permission denied - don't retry
                    return null;
                }
            }

            return null; // Failed after retries
        }

        private void ProcessSaveFile(FileInfo fileInfo, ref bool anyWarning)
        {
            string fileName = fileInfo.Name;
            long fileSize = fileInfo.Length;
            long limit = FileConstants.MaxFileSize;
            double percentage = (double)fileSize / limit * 100;

            ProcessSaveFileData(fileName, fileSize, limit, percentage);

            // Check for warning status
            if (fileSize >= FileConstants.DangerThreshold)
            {
                anyWarning = true;

                // Show overlay warning
                if (!overlayShown && overlayEnabled)
                {
                    overlayManager.ShowWarning(fileName, fileSize);
                    overlayShown = true;
                }

                // Trigger automatic backup
                if (backupSettings != null && backupSettings.AutoBackupOnDanger)
                {
                    string fullPath = Path.Combine(savePath, fileName);
                    if (backupManager != null && backupManager.CanCreateBackup(fullPath, BackupTrigger.DangerThreshold))
                    {
                        // Fire and forget - don't block monitoring
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await backupManager.CreateBackupAsync(fullPath, BackupTrigger.DangerThreshold);
                            }
                            catch (Exception ex)
                            {
                                LogError($"자동 백업 실패: {fileName}", ex);
                            }
                        });
                    }
                }
            }
        }

        private void ProcessSaveFileData(string fileName, long fileSize, long limit, double percentage)
        {
            string status;
            Color rowColor;

            if (fileSize >= FileConstants.DangerThreshold)
            {
                status = "위험";
                rowColor = UIConstants.DangerColor;
            }
            else if (fileSize >= FileConstants.WarningThreshold)
            {
                status = "주의";
                rowColor = UIConstants.WarningColor;
            }
            else
            {
                status = "안전";
                rowColor = UIConstants.SafeColor;
            }

            AddDataGridRow(fileName, fileSize, limit, percentage, status, rowColor);
        }

        private void AddDataGridRow(string fileName, long fileSize, long limit, double percentage, string status, Color rowColor)
        {
            int rowIndex = dgvFiles.Rows.Add(
                fileName,
                $"{fileSize} bytes",
                $"{limit} bytes",
                $"{percentage:F1}%",
                percentage,
                status
            );

            dgvFiles.Rows[rowIndex].DefaultCellStyle.BackColor = rowColor;
            dgvFiles.Rows[rowIndex].DefaultCellStyle.SelectionBackColor = Color.FromArgb(
                Math.Max(0, rowColor.R - 30),
                Math.Max(0, rowColor.G - 30),
                Math.Max(0, rowColor.B - 30)
            );

            if (fileSize >= FileConstants.DangerThreshold)
            {
                dgvFiles.Rows[rowIndex].DefaultCellStyle.Font = new Font(dgvFiles.Font, FontStyle.Bold);
                dgvFiles.Rows[rowIndex].DefaultCellStyle.ForeColor = Color.DarkRed;
            }
        }
        #endregion

        #region UI Rendering
        private void DgvFiles_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            // 진행바 컬럼만 커스텀 그리기
            if (e.ColumnIndex == 4 && e.RowIndex >= 0)
            {
                e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);

                double percentage = Convert.ToDouble(e.Value);
                int barWidth = (int)(e.CellBounds.Width * percentage / 100);

                // 진행바 배경
                using (SolidBrush bgBrush = new SolidBrush(Color.LightGray))
                {
                    e.Graphics.FillRectangle(bgBrush, e.CellBounds.X + 2, e.CellBounds.Y + 2,
                        e.CellBounds.Width - 4, e.CellBounds.Height - 4);
                }

                // 진행바 색상 결정
                Color barColor = UIConstants.ProgressBarSafe;
                if (percentage >= FileConstants.DangerPercentage)
                {
                    barColor = UIConstants.ProgressBarDanger;
                }
                else if (percentage >= FileConstants.WarningPercentage)
                {
                    barColor = UIConstants.ProgressBarWarning;
                }

                // 진행바 그리기
                using (SolidBrush barBrush = new SolidBrush(barColor))
                {
                    e.Graphics.FillRectangle(barBrush, e.CellBounds.X + 2, e.CellBounds.Y + 2,
                        barWidth - 4, e.CellBounds.Height - 4);
                }

                // 텍스트 그리기
                string text = $"{percentage:F1}%";
                using (SolidBrush textBrush = new SolidBrush(Color.Black))
                {
                    StringFormat format = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    e.Graphics.DrawString(text, e.CellStyle.Font, textBrush, e.CellBounds, format);
                }

                e.Handled = true;
            }
        }
        #endregion

        #region Utility Methods
        private bool ValidateSavePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                UpdateStatus("세이브 경로가 설정되지 않았습니다", Color.Red);
                return false;
            }

            try
            {
                // Check path format validity
                Path.GetFullPath(path);

                if (!Directory.Exists(path))
                {
                    UpdateStatus($"경로가 존재하지 않습니다: {path}", Color.Red);
                    return false;
                }

                // Test read permissions
                Directory.GetFiles(path, "*.d2s", SearchOption.TopDirectoryOnly);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                UpdateStatus("세이브 폴더 접근 권한이 없습니다", Color.Red);
                return false;
            }
            catch (PathTooLongException)
            {
                UpdateStatus("경로가 너무 깁니다", Color.Red);
                return false;
            }
            catch (Exception ex)
            {
                UpdateStatus($"경로 검증 실패: {ex.Message}", Color.Red);
                return false;
            }
        }

        private void UpdateStatus(string message, Color color)
        {
            if (lblStatus.InvokeRequired)
            {
                lblStatus.Invoke(new Action(() => UpdateStatus(message, color)));
                return;
            }

            lblStatus.Text = message;
            lblStatus.ForeColor = color;
        }

        private void LogError(string message, Exception ex)
        {
            try
            {
                string logDir = Path.GetDirectoryName(LogFilePath);
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

                if (ex != null)
                {
                    logEntry += $"\nException: {ex.GetType().Name}\n{ex.Message}\n{ex.StackTrace}";
                }

                logEntry += "\n---\n";

                File.AppendAllText(LogFilePath, logEntry);

                // Also write to debug output
                System.Diagnostics.Debug.WriteLine(logEntry);
            }
            catch
            {
                // Logging failed - nothing we can do, suppress silently
            }
        }

        private void HandleCriticalError(string context, Exception ex)
        {
            LogError($"CRITICAL: {context}", ex);

            string message = $"{context}\n\n" +
                            $"오류: {ex.Message}\n\n" +
                            $"로그 위치: {LogFilePath}\n\n" +
                            "계속 진행하시겠습니까?";

            var result = MessageBox.Show(
                message,
                "심각한 오류",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Error
            );

            if (result == DialogResult.No)
            {
                Application.Exit();
            }
        }
        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                StopMonitoring();
            }
            catch (Exception ex)
            {
                LogError("모니터링 중지 오류", ex);
            }

            try
            {
                StopPeriodicBackupTimer();
            }
            catch (Exception ex)
            {
                LogError("백업 타이머 중지 오류", ex);
            }

            try
            {
                backupManager?.Dispose();
            }
            catch (Exception ex)
            {
                LogError("백업 매니저 정리 오류", ex);
            }

            try
            {
                overlayManager?.Dispose();
            }
            catch (Exception ex)
            {
                LogError("오버레이 정리 오류", ex);
            }

            base.OnFormClosing(e);
        }
    }

    #region Overlay Manager
    /// <summary>
    /// Manages overlay warning display with reusable timer pattern
    /// </summary>
    public class OverlayManager : IDisposable
    {
        private OverlayWarningForm currentOverlay;
        private System.Windows.Forms.Timer autoCloseTimer;
        private readonly object overlayLock = new object();
        private bool disposed = false;

        public OverlayManager()
        {
            InitializeTimer();
        }

        private void InitializeTimer()
        {
            autoCloseTimer = new System.Windows.Forms.Timer
            {
                Interval = TimingConstants.OverlayDisplayDurationMs
            };
            autoCloseTimer.Tick += AutoCloseTimer_Tick;
        }

        public void ShowWarning(string fileName, long fileSize)
        {
            lock (overlayLock)
            {
                // Close existing overlay if present
                CloseCurrentOverlay();

                // Create new overlay
                currentOverlay = new OverlayWarningForm(fileName, fileSize);
                currentOverlay.Show();

                // Start auto-close timer
                autoCloseTimer.Stop();
                autoCloseTimer.Start();
            }
        }

        private void AutoCloseTimer_Tick(object sender, EventArgs e)
        {
            lock (overlayLock)
            {
                autoCloseTimer.Stop();
                CloseCurrentOverlay();
            }
        }

        private void CloseCurrentOverlay()
        {
            if (currentOverlay != null && !currentOverlay.IsDisposed)
            {
                currentOverlay.Close();
                currentOverlay.Dispose();
                currentOverlay = null;
            }
        }

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
                    lock (overlayLock)
                    {
                        autoCloseTimer?.Stop();
                        autoCloseTimer?.Dispose();
                        CloseCurrentOverlay();
                    }
                }
                disposed = true;
            }
        }
    }
    #endregion

    #region Backup System Classes
    /// <summary>
    /// Backup trigger reasons
    /// </summary>
    public enum BackupTrigger
    {
        DangerThreshold,
        PeriodicAutomatic,
        ManualSingle,
        ManualBulk,
        PreRestore
    }

    /// <summary>
    /// Backup validation status
    /// </summary>
    public enum BackupStatus
    {
        Valid,
        Corrupted,
        Missing,
        Restored
    }

    /// <summary>
    /// Backup settings (persisted to registry)
    /// </summary>
    public class BackupSettings
    {
        public bool AutoBackupOnDanger { get; set; } = true;
        public bool PeriodicBackupEnabled { get; set; } = false;
        public int PeriodicIntervalMinutes { get; set; } = 30;
        public int MaxBackupsPerFile { get; set; } = 10;
        public int BackupCooldownSeconds { get; set; } = 60;
        public string CustomBackupPath { get; set; }

        private const string BackupRegistryKey = @"Software\D2RMonitor\Backup";

        public static BackupSettings LoadFromRegistry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(BackupRegistryKey))
                {
                    if (key == null) return new BackupSettings();

                    return new BackupSettings
                    {
                        AutoBackupOnDanger = (int)key.GetValue("AutoBackupOnDanger", 1) == 1,
                        PeriodicBackupEnabled = (int)key.GetValue("PeriodicEnabled", 0) == 1,
                        PeriodicIntervalMinutes = (int)key.GetValue("PeriodicInterval", 30),
                        MaxBackupsPerFile = (int)key.GetValue("MaxBackups", 10),
                        BackupCooldownSeconds = (int)key.GetValue("CooldownSeconds", 60),
                        CustomBackupPath = (string)key.GetValue("CustomPath", "")
                    };
                }
            }
            catch
            {
                return new BackupSettings();
            }
        }

        public void SaveToRegistry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(BackupRegistryKey))
                {
                    if (key == null) return;

                    key.SetValue("AutoBackupOnDanger", AutoBackupOnDanger ? 1 : 0);
                    key.SetValue("PeriodicEnabled", PeriodicBackupEnabled ? 1 : 0);
                    key.SetValue("PeriodicInterval", PeriodicIntervalMinutes);
                    key.SetValue("MaxBackups", MaxBackupsPerFile);
                    key.SetValue("CooldownSeconds", BackupCooldownSeconds);
                    key.SetValue("CustomPath", CustomBackupPath ?? "");
                }
            }
            catch
            {
                // Settings save failed - non-critical
            }
        }
    }

    /// <summary>
    /// Backup metadata for a single backup file
    /// </summary>
    public class BackupMetadata
    {
        public string OriginalFile { get; set; }
        public string BackupFile { get; set; }
        public DateTime Timestamp { get; set; }
        public long FileSize { get; set; }
        public BackupTrigger TriggerReason { get; set; }
        public bool IsAutomatic { get; set; }
        public BackupStatus Status { get; set; }

        public string GetDisplayName()
        {
            return $"{Path.GetFileNameWithoutExtension(OriginalFile)} - {Timestamp:yyyy-MM-dd HH:mm:ss}";
        }
    }

    /// <summary>
    /// Result of a backup operation
    /// </summary>
    public class BackupResult
    {
        public bool Success { get; set; }
        public BackupMetadata Metadata { get; set; }
        public string ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Result of a restore operation
    /// </summary>
    public class RestoreResult
    {
        public bool Success { get; set; }
        public string RestoredFile { get; set; }
        public BackupMetadata PreRestoreBackup { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Event args for backup events
    /// </summary>
    public class BackupEventArgs : EventArgs
    {
        public string FileName { get; set; }
        public BackupTrigger Trigger { get; set; }
    }

    public class BackupErrorEventArgs : EventArgs
    {
        public string FileName { get; set; }
        public Exception Exception { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class BackupProgressEventArgs : EventArgs
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string CurrentFile { get; set; }
    }
    #endregion

    #region Overlay Warning Form
    /// <summary>
    /// 오버레이 경고 폼
    /// </summary>
    public class OverlayWarningForm : Form
    {
        // Win32 API constants
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;

        public OverlayWarningForm(string fileName, long fileSize)
        {
            InitializeWarningForm(fileName, fileSize);
        }

        private void InitializeWarningForm(string fileName, long fileSize)
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(UIConstants.OverlayWidth, UIConstants.OverlayHeight);
            BackColor = Color.DarkRed;
            Opacity = 0.9;

            // Safely apply click-through transparency
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    int initialStyle = GetWindowLong(Handle, GWL_EXSTYLE);

                    if (initialStyle != 0) // Verify GetWindowLong succeeded
                    {
                        SetWindowLong(Handle, GWL_EXSTYLE,
                                     initialStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                    }
                }
            }
            catch (Exception ex)
            {
                // Non-critical - overlay still works, just not click-through
                System.Diagnostics.Debug.WriteLine($"Win32 style error: {ex.Message}");
            }

            // Position overlay
            try
            {
                int screenWidth = Screen.PrimaryScreen.Bounds.Width;
                Location = new Point(
                    screenWidth - Width - UIConstants.OverlayMarginRight,
                    UIConstants.OverlayMarginTop
                );
            }
            catch
            {
                // Fallback to default position
                Location = new Point(100, 100);
            }

            // 메인 컨테이너
            Panel mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            Controls.Add(mainPanel);

            // 경고 텍스트 (한글)
            Label lblWarningKR = new Label
            {
                Text = "세이브 파일 크기 경고",
                ForeColor = Color.White,
                Font = new Font("맑은 고딕", 11, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(300, 25),
                Location = new Point(10, 10),
                TextAlign = ContentAlignment.MiddleCenter
            };
            mainPanel.Controls.Add(lblWarningKR);

            // 경고 텍스트 (영문)
            Label lblWarningEN = new Label
            {
                Text = "Save File Size Warning",
                ForeColor = Color.Yellow,
                Font = new Font("Arial", 9, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(300, 20),
                Location = new Point(10, 35),
                TextAlign = ContentAlignment.MiddleCenter
            };
            mainPanel.Controls.Add(lblWarningEN);

            // 파일 정보
            double percentage = (double)fileSize / FileConstants.MaxFileSize * 100;
            Label lblInfo = new Label
            {
                Text = $"{fileName}\n{fileSize}/{FileConstants.MaxFileSize} bytes ({percentage:F1}%)",
                ForeColor = Color.White,
                Font = new Font("Consolas", 8),
                AutoSize = false,
                Size = new Size(300, 35),
                Location = new Point(10, 60),
                TextAlign = ContentAlignment.MiddleCenter
            };
            mainPanel.Controls.Add(lblInfo);
        }

        // Win32 API 선언
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
    #endregion
}