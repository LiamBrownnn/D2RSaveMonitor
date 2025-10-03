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

        /// <summary>
        /// Buffer size for file copy operations (80KB)
        /// </summary>
        public const int FileCopyBufferSize = 81920; // 80 * 1024
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
        private FileMonitoringService fileMonitoringService;
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

        // Language UI Controls
        private ComboBox cboLanguage;
        private Label lblLanguage;

        // UI Control References for localization
        private Label lblPath;
        private Label lblProgressText;

        #endregion

        public Form1()
        {
            Logger.Info("Form1 초기화 시작 / Form1 initialization started");

            // Load language settings first
            LanguageManager.LoadLanguage();

            InitializeComponent();
            InitializeUI();
            LoadSettings();
            overlayManager = new OverlayManager();
            InitializeBackupSystem();
            InitializeFileMonitoring();
            StartMonitoring();

            // Subscribe to language change event
            LanguageManager.LanguageChanged += OnLanguageChanged;

            Logger.Info("Form1 초기화 완료 / Form1 initialization completed");
        }

        /// <summary>
        /// 파일 모니터링 서비스 초기화 / Initialize file monitoring service
        /// </summary>
        private void InitializeFileMonitoring()
        {
            fileMonitoringService = new FileMonitoringService();

            // Subscribe to monitoring events
            fileMonitoringService.FilesChanged += OnFilesChanged;
            fileMonitoringService.MonitoringError += OnMonitoringError;
            fileMonitoringService.StatusChanged += OnMonitoringStatusChanged;
        }

        private void InitializeUI()
        {
            // 폼 설정
            Text = LanguageManager.GetString("MainTitle");
            Size = new Size(900, 720);  // Increased height for backup panel
            StartPosition = FormStartPosition.CenterScreen;

            // 언어 설정 (우측 상단)
            lblLanguage = new Label
            {
                Text = LanguageManager.GetString("Language"),
                Location = new Point(680, 15),
                Size = new Size(60, 20),
                TextAlign = ContentAlignment.MiddleRight
            };
            Controls.Add(lblLanguage);

            cboLanguage = new ComboBox
            {
                Location = new Point(745, 12),
                Size = new Size(135, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboLanguage.Items.Add("English");
            cboLanguage.Items.Add("한국어");
            // Map Language enum to ComboBox index: English=0, Korean=1
            cboLanguage.SelectedIndex = (LanguageManager.CurrentLanguage == Language.English) ? 0 : 1;
            cboLanguage.SelectedIndexChanged += CboLanguage_SelectedIndexChanged;
            Controls.Add(cboLanguage);

            // 경로 레이블
            lblPath = new Label
            {
                Text = LanguageManager.GetString("SavePathLabel"),
                Location = new Point(10, 47),
                Size = new Size(120, 20)
            };
            Controls.Add(lblPath);

            // 경로 텍스트박스
            txtSavePath = new TextBox
            {
                Location = new Point(130, 44),
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
                Text = LanguageManager.GetString("BtnOpenSaveFolder"),
                Location = new Point(540, 42),
                Size = new Size(90, 28)
            };
            btnOpenSaveFolder.Click += BtnOpenSaveFolder_Click;
            toolTip.SetToolTip(btnOpenSaveFolder, LanguageManager.GetString("TipOpenSaveFolder"));
            Controls.Add(btnOpenSaveFolder);

            // 백업 폴더 열기 버튼
            btnOpenBackupFolder = new Button
            {
                Text = LanguageManager.GetString("BtnOpenBackupFolder"),
                Location = new Point(635, 42),
                Size = new Size(90, 28)
            };
            btnOpenBackupFolder.Click += BtnOpenBackupFolder_Click;
            toolTip.SetToolTip(btnOpenBackupFolder, LanguageManager.GetString("TipOpenBackupFolder"));
            Controls.Add(btnOpenBackupFolder);

            // 찾아보기 버튼
            btnBrowse = new Button
            {
                Text = LanguageManager.GetString("BtnBrowse"),
                Location = new Point(730, 42),
                Size = new Size(150, 28)
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
                Location = new Point(10, 80),
                Size = new Size(870, 390),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,  // Ctrl 키로 여러 파일 선택 가능
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToResizeColumns = false,  // 컬럼 너비 조절 금지 (가로)
                AllowUserToResizeRows = false,  // 행 높이 조절 금지 (세로)
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing  // 헤더 높이 고정
            };
            dgvFiles.CellPainting += DgvFiles_CellPainting;
            dgvFiles.SelectionChanged += DgvFiles_SelectionChanged;

            // 컬럼 설정
            dgvFiles.Columns.Add("FileName", LanguageManager.GetString("ColFileName"));
            dgvFiles.Columns.Add("CurrentSize", LanguageManager.GetString("ColCurrentSize"));
            dgvFiles.Columns.Add("Limit", LanguageManager.GetString("ColLimit"));
            dgvFiles.Columns.Add("Percentage", LanguageManager.GetString("ColPercentage"));
            dgvFiles.Columns.Add("ProgressBar", LanguageManager.GetString("ColProgressBar"));
            dgvFiles.Columns.Add("Status", LanguageManager.GetString("ColStatus"));

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
                Text = LanguageManager.GetString("GrpBackup"),
                Location = new Point(10, 480),
                Size = new Size(870, 160),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            Controls.Add(grpBackup);

            // 백업 선택 버튼
            btnBackupSelected = new Button
            {
                Text = LanguageManager.GetString("BtnBackupSelected"),
                Location = new Point(10, 25),
                Size = new Size(120, 30),
                Enabled = false
            };
            btnBackupSelected.Click += BtnBackupSelected_Click;
            grpBackup.Controls.Add(btnBackupSelected);

            // 전체 백업 버튼
            btnBackupAll = new Button
            {
                Text = LanguageManager.GetString("BtnBackupAll"),
                Location = new Point(140, 25),
                Size = new Size(120, 30)
            };
            btnBackupAll.Click += BtnBackupAll_Click;
            grpBackup.Controls.Add(btnBackupAll);

            // 복원 버튼
            btnViewBackups = new Button
            {
                Text = LanguageManager.GetString("BtnViewBackups"),
                Location = new Point(270, 25),
                Size = new Size(120, 30)
            };
            btnViewBackups.Click += BtnViewBackups_Click;
            grpBackup.Controls.Add(btnViewBackups);

            // 설정 버튼
            btnBackupSettings = new Button
            {
                Text = LanguageManager.GetString("BtnBackupSettings"),
                Location = new Point(400, 25),
                Size = new Size(120, 30)
            };
            btnBackupSettings.Click += BtnBackupSettings_Click;
            grpBackup.Controls.Add(btnBackupSettings);

            // 마지막 백업 레이블
            lblLastBackup = new Label
            {
                Text = LanguageManager.GetString("LblLastBackup") + ": " + LanguageManager.GetString("None"),
                Location = new Point(10, 65),
                AutoSize = true
            };
            grpBackup.Controls.Add(lblLastBackup);

            // 백업 상태 레이블
            lblBackupStatus = new Label
            {
                Text = "",  // Will be set by UpdateBackupStatusDisplay()
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
            lblProgressText = new Label
            {
                Name = "lblProgressText",
                Text = "",
                Location = new Point(10, 135),
                Size = new Size(850, 15),
                Visible = false
            };
            grpBackup.Controls.Add(lblProgressText);

            // 상태 레이블 (패널 아래로 이동)
            lblStatus = new Label
            {
                Text = LanguageManager.GetString("StatusMonitoring"),
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
                backupSettings = SettingsManager.LoadSettings();

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
                Logger.Error("백업 시스템 초기화 실패", ex);
                MessageBox.Show(
                    $"{LanguageManager.GetString("BackupInitFailed")}:\n{ex.Message}\n\n{LanguageManager.GetString("MonitoringContinues")}",
                    LanguageManager.GetString("Warning"),
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
                // null 체크
                if (backupSettings == null || backupManager == null)
                {
                    Logger.Warning("주기적 백업 스킵: 백업 시스템이 초기화되지 않음");
                    return;
                }

                if (!backupSettings.PeriodicBackupEnabled) return;

                var candidates = Directory.GetFiles(savePath, "*.d2s")
                    .Select(filePath => new FileInfo(filePath))
                    .Where(info => info.Exists)
                    .Where(info =>
                    {
                        switch (backupSettings.PeriodicScope)
                        {
                            case PeriodicBackupScope.DangerOnly:
                                return info.Length >= FileConstants.DangerThreshold;
                            case PeriodicBackupScope.WarningOrAbove:
                                return info.Length >= FileConstants.WarningThreshold;
                            case PeriodicBackupScope.EntireRange:
                            default:
                                return info.Length > 0;
                        }
                    })
                    .Select(info => info.FullName)
                    .ToList();

                if (candidates.Any())
                {
                    await backupManager.CreateBulkBackupAsync(candidates, BackupTrigger.PeriodicAutomatic);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("주기적 백업 실패", ex);
            }
        }

        private void UpdateBackupStatusDisplay()
        {
            if (lblBackupStatus.InvokeRequired)
            {
                lblBackupStatus.Invoke(new Action(UpdateBackupStatusDisplay));
                return;
            }

            string autoBackupLine = backupSettings.AutoBackupOnDanger
                ? LanguageManager.GetString("AutoBackupDangerOn")
                : LanguageManager.GetString("AutoBackupDangerOff");

            string periodicLine;
            if (backupSettings.PeriodicBackupEnabled)
            {
                string everyMinutes = LanguageManager.CurrentLanguage == Language.Korean
                    ? $"{backupSettings.PeriodicIntervalMinutes}분"
                    : $"{backupSettings.PeriodicIntervalMinutes} min";
                string rangeKey = GetPeriodicRangeKey(backupSettings.PeriodicScope);
                string scopeText = LanguageManager.GetString(rangeKey);
                periodicLine = string.Format(
                    LanguageManager.GetString("PeriodicBackupOnSummary"),
                    everyMinutes,
                    scopeText
                );
            }
            else
            {
                periodicLine = LanguageManager.GetString("PeriodicBackupOffSummary");
            }

            var lines = new List<string>
            {
                autoBackupLine,
                periodicLine
            };

            string backupLocation = backupManager != null
                ? backupManager.BackupDirectory
                : (!string.IsNullOrWhiteSpace(savePath) ? Path.Combine(savePath, "Backups") : string.Empty);

            if (!string.IsNullOrWhiteSpace(backupLocation))
            {
                lines.Add(string.Format(LanguageManager.GetString("BackupLocationInfo"), backupLocation));
            }

            lblBackupStatus.Text = string.Join(Environment.NewLine, lines);
        }

        private string GetPeriodicRangeKey(PeriodicBackupScope scope)
        {
            switch (scope)
            {
                case PeriodicBackupScope.DangerOnly:
                    return "PeriodicRangeDanger";
                case PeriodicBackupScope.EntireRange:
                    return "PeriodicRangeAll";
                default:
                    return "PeriodicRangeWarning";
            }
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
            Logger.Error($"백업 실패: {e.FileName}", e.Exception);
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
                // Start monitoring service
                fileMonitoringService.StartMonitoring(savePath);

                // Initial scan
                CheckSaveFiles();
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
                fileMonitoringService?.StopMonitoring();
            }
            catch (Exception ex)
            {
                Logger.Error("Monitoring cleanup error", ex);
            }
        }

        /// <summary>
        /// 파일 변경 이벤트 핸들러 / Files changed event handler
        /// </summary>
        private void OnFilesChanged(object sender, FileChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnFilesChanged(sender, e)));
                return;
            }

            // 파일 변경 감지 시 전체 스캔 수행
            CheckSaveFiles();
        }

        /// <summary>
        /// 모니터링 오류 이벤트 핸들러 / Monitoring error event handler
        /// </summary>
        private void OnMonitoringError(object sender, MonitoringStatusEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnMonitoringError(sender, e)));
                return;
            }

            UpdateStatus(e.Message, Color.Red);
            Logger.Error(e.Message);
        }

        /// <summary>
        /// 모니터링 상태 변경 이벤트 핸들러 / Monitoring status changed event handler
        /// </summary>
        private void OnMonitoringStatusChanged(object sender, MonitoringStatusEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnMonitoringStatusChanged(sender, e)));
                return;
            }

            UpdateStatus(e.Message, e.IsError ? Color.Red : Color.Green);
        }

        private void HandleMonitoringError(string message)
        {
            UpdateStatus(message, Color.Red);
            Logger.Error(message);
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
                Logger.Warning("레지스트리 접근 거부 - 기본값 사용");
            }
            catch (Exception ex)
            {
                savePath = GetDefaultSavePath();
                txtSavePath.Text = savePath;
                Logger.Error("설정 로드 실패", ex);
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
                Logger.Warning("레지스트리 쓰기 권한 없음");
            }
            catch (Exception ex)
            {
                Logger.Error("설정 저장 실패", ex);
                MessageBox.Show(
                    string.Format(LanguageManager.GetString("SettingsSaveFailed"), ex.Message),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
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
                dialog.Description = LanguageManager.GetString("SelectSaveFolder");
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
                        LanguageManager.GetString("SavePathNotSet"),
                        LanguageManager.GetString("Notice"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return;
                }

                // 보안 검증 및 안전한 폴더 열기
                if (!SecurityHelper.OpenDirectoryInExplorer(savePath, out string errorMessage))
                {
                    MessageBox.Show(
                        errorMessage ?? LanguageManager.GetString("CannotOpenFolder"),
                        LanguageManager.GetString("Error"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    Logger.Error("세이브 폴더 열기 실패", new InvalidOperationException(errorMessage));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format("{0}:\n{1}", LanguageManager.GetString("CannotOpenFolder"), ex.Message),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                Logger.Error("세이브 폴더 열기 실패", ex);
            }
        }

        private void BtnOpenBackupFolder_Click(object sender, EventArgs e)
        {
            try
            {
                if (backupManager == null)
                {
                    MessageBox.Show(
                        LanguageManager.GetString("BackupSystemNotInitialized"),
                        LanguageManager.GetString("Notice"),
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

                // 보안 검증 및 안전한 폴더 열기
                if (!SecurityHelper.OpenDirectoryInExplorer(backupDirectory, out string errorMessage))
                {
                    MessageBox.Show(
                        errorMessage ?? LanguageManager.GetString("CannotOpenBackupFolder"),
                        LanguageManager.GetString("Error"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    Logger.Error("백업 폴더 열기 실패", new InvalidOperationException(errorMessage));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format("{0}:\n{1}", LanguageManager.GetString("CannotOpenBackupFolder"), ex.Message),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                Logger.Error("백업 폴더 열기 실패", ex);
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
                btnBackupSelected.Text = string.Format(LanguageManager.GetString("BtnBackupSelectedWithCount"), selectedCount);
            }
            else
            {
                btnBackupSelected.Text = LanguageManager.GetString("BtnBackupSelected");
            }
        }

        private async void BtnBackupSelected_Click(object sender, EventArgs e)
        {
            if (dgvFiles.SelectedRows.Count == 0) return;

            // backupManager null 체크
            if (backupManager == null)
            {
                MessageBox.Show(
                    LanguageManager.GetString("BackupSystemNotInitialized"),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

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
                            string.Format(LanguageManager.GetString("BackupSuccessWithTime"),
                                Path.GetFileName(selectedFiles[0]),
                                result.Duration.TotalMilliseconds.ToString("F0")),
                            LanguageManager.GetString("BackupSuccessTitle"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }
                    else
                    {
                        MessageBox.Show(
                            string.Format(LanguageManager.GetString("BackupFailedWithError"),
                                Path.GetFileName(selectedFiles[0]),
                                result.ErrorMessage),
                            LanguageManager.GetString("BackupFailedTitle"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                    }
                }
                // 여러 파일인 경우
                else
                {
                    var confirmResult = MessageBox.Show(
                        string.Format(LanguageManager.GetString("ConfirmSelectedBackup"), selectedFiles.Count),
                        LanguageManager.GetString("ConfirmSelectedBackupTitle"),
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    if (confirmResult != DialogResult.Yes) return;

                    // 벌크 백업 실행
                    var results = await backupManager.CreateBulkBackupAsync(selectedFiles, BackupTrigger.ManualBulk);

                    int successCount = results.Count(r => r.Success);
                    int failCount = results.Count(r => !r.Success);

                    string message = string.Format(LanguageManager.GetString("BackupCompleteStats"), successCount);
                    if (failCount > 0)
                    {
                        message += string.Format(LanguageManager.GetString("BackupStatsWithFail"), failCount);
                    }

                    MessageBox.Show(
                        message,
                        LanguageManager.GetString("SelectedBackupCompleteTitle"),
                        MessageBoxButtons.OK,
                        failCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LanguageManager.GetString("BackupErrorOccurred"), ex.Message),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                Logger.Error("수동 백업 실패", ex);
            }
            finally
            {
                btnBackupSelected.Enabled = dgvFiles.SelectedRows.Count > 0;
                btnBackupAll.Enabled = true;
            }
        }

        private async void BtnBackupAll_Click(object sender, EventArgs e)
        {
            // backupManager null 체크
            if (backupManager == null)
            {
                MessageBox.Show(
                    LanguageManager.GetString("BackupSystemNotInitialized"),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            try
            {
                // 파일 목록 가져오기 및 확장자 검증
                var allFiles = Directory.GetFiles(savePath, "*.d2s");
                var files = SecurityHelper.FilterValidSaveFiles(allFiles).ToList();

                if (files.Count == 0)
                {
                    MessageBox.Show(
                        LanguageManager.GetString("NoFilesToBackup"),
                        LanguageManager.GetString("Notice"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return;
                }

                var confirmResult = MessageBox.Show(
                    string.Format(LanguageManager.GetString("ConfirmBackupAll"), files.Count),
                    LanguageManager.GetString("ConfirmBackupAllTitle"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (confirmResult != DialogResult.Yes) return;

                btnBackupSelected.Enabled = false;
                btnBackupAll.Enabled = false;

                var results = await backupManager.CreateBulkBackupAsync(files, BackupTrigger.ManualBulk);

                int successCount = results.Count(r => r.Success);
                int failCount = results.Count(r => !r.Success);

                string message = string.Format(LanguageManager.GetString("BackupCompleteStats"), successCount);
                if (failCount > 0)
                {
                    message += string.Format(LanguageManager.GetString("BackupStatsWithFail"), failCount);
                }

                MessageBox.Show(
                    message,
                    LanguageManager.GetString("BackupAllCompleteTitle"),
                    MessageBoxButtons.OK,
                    failCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LanguageManager.GetString("BackupAllError"), ex.Message),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                Logger.Error("전체 백업 실패", ex);
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
                    string.Format(LanguageManager.GetString("CannotOpenBackupList"), ex.Message),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                Logger.Error("백업 목록 열기 실패", ex);
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
                        backupSettings = SettingsManager.LoadSettings();

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
                    string.Format(LanguageManager.GetString("CannotOpenSettings"), ex.Message),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                Logger.Error("백업 설정 열기 실패", ex);
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

                string[] d2sFiles = GetD2SFiles();
                if (d2sFiles == null)
                {
                    return; // Error already handled
                }

                var result = ProcessAllSaveFiles(d2sFiles);
                UpdateCheckStatusDisplay(result);
            }
            catch (Exception ex)
            {
                HandleCriticalError("파일 체크 중 심각한 오류", ex);
            }
        }

        /// <summary>
        /// 세이브 폴더에서 .d2s 파일 목록 가져오기 / Get .d2s files from save folder
        /// </summary>
        private string[] GetD2SFiles()
        {
            try
            {
                // 파일 목록 가져오기 및 확장자 검증
                var allFiles = Directory.GetFiles(savePath, "*.d2s");
                return SecurityHelper.FilterValidSaveFiles(allFiles);
            }
            catch (UnauthorizedAccessException)
            {
                UpdateStatus("세이브 폴더 접근 권한 없음", Color.Red);
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                UpdateStatus("세이브 폴더를 찾을 수 없음", Color.Red);
                return null;
            }
        }

        /// <summary>
        /// 파일 처리 결과 / File processing result
        /// </summary>
        private struct FileProcessingResult
        {
            public bool AnyWarning;
            public int SuccessCount;
            public int ErrorCount;
        }

        /// <summary>
        /// 모든 세이브 파일 처리 / Process all save files
        /// </summary>
        private FileProcessingResult ProcessAllSaveFiles(string[] filePaths)
        {
            var result = new FileProcessingResult
            {
                AnyWarning = false,
                SuccessCount = 0,
                ErrorCount = 0
            };

            foreach (string filePath in filePaths)
            {
                try
                {
                    FileInfo fileInfo = GetFileInfoWithRetryAsync(filePath).GetAwaiter().GetResult();
                    if (fileInfo == null)
                    {
                        result.ErrorCount++;
                        continue;
                    }

                    ProcessSaveFile(fileInfo, ref result.AnyWarning);
                    result.SuccessCount++;
                }
                catch (FileNotFoundException)
                {
                    // File deleted between enumeration and processing - ignore
                    continue;
                }
                catch (UnauthorizedAccessException ex)
                {
                    Logger.Warning($"파일 접근 거부: {Path.GetFileName(filePath)}", ex);
                    result.ErrorCount++;
                }
                catch (IOException ex)
                {
                    Logger.Error($"파일 읽기 실패: {Path.GetFileName(filePath)}", ex);
                    result.ErrorCount++;
                }
                catch (Exception ex)
                {
                    Logger.Error($"예상치 못한 오류: {Path.GetFileName(filePath)}", ex);
                    result.ErrorCount++;
                }
            }

            return result;
        }

        /// <summary>
        /// 체크 완료 후 상태 표시 업데이트 / Update status display after check
        /// </summary>
        private void UpdateCheckStatusDisplay(FileProcessingResult result)
        {
            if (!result.AnyWarning)
            {
                overlayShown = false;
            }

            string statusMessage = $"마지막 체크: {DateTime.Now:HH:mm:ss} - {result.SuccessCount}개 파일 처리됨";

            if (result.ErrorCount > 0)
            {
                statusMessage += $" ({result.ErrorCount}개 오류)";
            }

            UpdateStatus(statusMessage, result.AnyWarning ? Color.Red : Color.Green);
        }

        private async Task<FileInfo> GetFileInfoWithRetryAsync(string filePath)
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
                    await Task.Delay(TimingConstants.FileAccessRetryDelayMs); // Thread.Sleep에서 Task.Delay로 변경
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
                                Logger.Error($"자동 백업 실패: {fileName}", ex);
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
                status = LanguageManager.GetString("StatusDanger");
                rowColor = UIConstants.DangerColor;
            }
            else if (fileSize >= FileConstants.WarningThreshold)
            {
                status = LanguageManager.GetString("StatusWarning");
                rowColor = UIConstants.WarningColor;
            }
            else
            {
                status = LanguageManager.GetString("StatusSafe");
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

                double percentage = Math.Max(0, Convert.ToDouble(e.Value));
                int availableWidth = Math.Max(0, e.CellBounds.Width - 4);
                int barWidth = (int)Math.Round(availableWidth * percentage / 100.0);
                barWidth = Math.Min(availableWidth, Math.Max(0, barWidth));

                // 진행바 배경
                using (SolidBrush bgBrush = new SolidBrush(Color.LightGray))
                {
                    e.Graphics.FillRectangle(bgBrush, e.CellBounds.X + 2, e.CellBounds.Y + 2,
                        availableWidth, e.CellBounds.Height - 4);
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
                if (barWidth > 0)
                {
                    using (SolidBrush barBrush = new SolidBrush(barColor))
                    {
                        e.Graphics.FillRectangle(barBrush, e.CellBounds.X + 2, e.CellBounds.Y + 2,
                            barWidth, e.CellBounds.Height - 4);
                    }
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

        private void HandleCriticalError(string context, Exception ex)
        {
            Logger.Critical($"{context}", ex);

            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "D2RSaveMonitor", "Logs");

            string message = string.Format(LanguageManager.GetString("CriticalErrorMessage"),
                context, ex.Message, logDir);

            var result = MessageBox.Show(
                message,
                LanguageManager.GetString("CriticalErrorTitle"),
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
                Logger.Error("모니터링 중지 오류", ex);
            }

            try
            {
                StopPeriodicBackupTimer();
            }
            catch (Exception ex)
            {
                Logger.Error("백업 타이머 중지 오류", ex);
            }

            try
            {
                backupManager?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error("백업 매니저 정리 오류", ex);
            }

            try
            {
                overlayManager?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error("오버레이 정리 오류", ex);
            }

            base.OnFormClosing(e);
        }

        /// <summary>
        /// 리소스 정리 및 이벤트 구독 해제 / Resource cleanup and event unsubscription
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose managed components
                if (components != null)
                {
                    components.Dispose();
                }

                // 이벤트 구독 해제 / Unsubscribe from events
                try
                {
                    LanguageManager.LanguageChanged -= OnLanguageChanged;
                }
                catch (Exception ex)
                {
                    Logger.Error("LanguageChanged 이벤트 구독 해제 실패", ex);
                }

                // BackupManager 이벤트 구독 해제
                if (backupManager != null)
                {
                    try
                    {
                        backupManager.BackupStarted -= OnBackupStarted;
                        backupManager.BackupCompleted -= OnBackupCompleted;
                        backupManager.BackupFailed -= OnBackupFailed;
                        backupManager.BackupProgress -= OnBackupProgress;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("BackupManager 이벤트 구독 해제 실패", ex);
                    }
                }

                // FileMonitoringService 이벤트 구독 해제
                if (fileMonitoringService != null)
                {
                    try
                    {
                        fileMonitoringService.FilesChanged -= OnFilesChanged;
                        fileMonitoringService.MonitoringError -= OnMonitoringError;
                        fileMonitoringService.StatusChanged -= OnMonitoringStatusChanged;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("FileMonitoringService 이벤트 구독 해제 실패", ex);
                    }
                }

                // IDisposable 리소스 정리
                try
                {
                    backupManager?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Error("BackupManager Dispose 실패", ex);
                }

                try
                {
                    overlayManager?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Error("OverlayManager Dispose 실패", ex);
                }

                try
                {
                    periodicBackupTimer?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Error("PeriodicBackupTimer Dispose 실패", ex);
                }

                try
                {
                    fileMonitoringService?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Error("FileMonitoringService Dispose 실패", ex);
                }
            }

            base.Dispose(disposing);
        }

        #region Language Event Handlers
        /// <summary>
        /// 언어 변경 이벤트 핸들러 / Language changed event handler
        /// </summary>
        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnLanguageChanged(sender, e)));
                return;
            }

            // Update all UI text with new language
            UpdateUILanguage();
        }

        /// <summary>
        /// 언어 선택 변경 이벤트 / Language selection changed event
        /// </summary>
        private void CboLanguage_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Map ComboBox index to Language enum: 0=English, 1=Korean
            Language newLanguage = (cboLanguage.SelectedIndex == 0) ? Language.English : Language.Korean;
            LanguageManager.CurrentLanguage = newLanguage;
        }

        /// <summary>
        /// 모든 UI 텍스트 업데이트 / Update all UI text
        /// </summary>
        private void UpdateUILanguage()
        {
            // Main form
            Text = LanguageManager.GetString("MainTitle");

            // Labels
            lblLanguage.Text = LanguageManager.GetString("Language");
            lblPath.Text = LanguageManager.GetString("SavePathLabel");

            // Buttons
            btnOpenSaveFolder.Text = LanguageManager.GetString("BtnOpenSaveFolder");
            btnOpenBackupFolder.Text = LanguageManager.GetString("BtnOpenBackupFolder");
            btnBrowse.Text = LanguageManager.GetString("BtnBrowse");

            // DataGridView columns
            dgvFiles.Columns["FileName"].HeaderText = LanguageManager.GetString("ColFileName");
            dgvFiles.Columns["CurrentSize"].HeaderText = LanguageManager.GetString("ColCurrentSize");
            dgvFiles.Columns["Limit"].HeaderText = LanguageManager.GetString("ColLimit");
            dgvFiles.Columns["Percentage"].HeaderText = LanguageManager.GetString("ColPercentage");
            dgvFiles.Columns["ProgressBar"].HeaderText = LanguageManager.GetString("ColProgressBar");
            dgvFiles.Columns["Status"].HeaderText = LanguageManager.GetString("ColStatus");

            // Backup panel
            grpBackup.Text = LanguageManager.GetString("GrpBackup");
            btnBackupSelected.Text = LanguageManager.GetString("BtnBackupSelected");
            btnBackupAll.Text = LanguageManager.GetString("BtnBackupAll");
            btnViewBackups.Text = LanguageManager.GetString("BtnViewBackups");
            btnBackupSettings.Text = LanguageManager.GetString("BtnBackupSettings");

            // Update backup status display
            UpdateBackupStatusDisplay();

            // Update last backup label if needed
            if (lblLastBackup.Text.Contains("없음") || lblLastBackup.Text.Contains("None"))
            {
                lblLastBackup.Text = LanguageManager.GetString("LblLastBackup") + ": " + LanguageManager.GetString("None");
            }

            // Status label
            lblStatus.Text = LanguageManager.GetString("StatusMonitoring");

            // Refresh the grid to update status text in current language
            CheckSaveFiles();
        }
        #endregion
    }

    #region Overlay Manager
    /// <summary>
    /// Manages overlay warning display with reusable timer pattern
    /// </summary>
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
    /// Periodic backup scope
    /// </summary>
    public enum PeriodicBackupScope
    {
        DangerOnly = 0,
        WarningOrAbove = 1,
        EntireRange = 2
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
        public PeriodicBackupScope PeriodicScope { get; set; } = PeriodicBackupScope.EntireRange;
        public int PeriodicIntervalMinutes { get; set; } = 30;
        public int MaxBackupsPerFile { get; set; } = 10;
        public int BackupCooldownSeconds { get; set; } = 60;
        public string CustomBackupPath { get; set; }
        public bool EnableCompression { get; set; } = true;  // 백업 압축 활성화 (기본: 활성화)

        // 레지스트리 메서드는 더 이상 사용하지 않음 - SettingsManager 사용
        // Registry methods are deprecated - use SettingsManager instead

        [Obsolete("Use SettingsManager.LoadSettings() instead")]
        private const string BackupRegistryKey = @"Software\D2RMonitor\Backup";

        [Obsolete("Use SettingsManager.LoadSettings() instead")]
        public static BackupSettings LoadFromRegistry()
        {
            // SettingsManager로 마이그레이션됨
            return SettingsManager.LoadSettings();
        }

        [Obsolete("Use SettingsManager.SaveSettings() instead")]
        public void SaveToRegistry()
        {
            // SettingsManager로 마이그레이션됨
            SettingsManager.SaveSettings(this);
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
        public bool IsCompressed { get; set; }  // 압축 여부
        public long CompressedSize { get; set; }  // 압축된 파일 크기
        public long OriginalSize { get; set; }  // 원본 파일 크기

        public string GetDisplayName()
        {
            return $"{Path.GetFileNameWithoutExtension(OriginalFile)} - {Timestamp:yyyy-MM-dd HH:mm:ss}";
        }

        public double GetCompressionRatio()
        {
            if (!IsCompressed || OriginalSize == 0) return 0;
            return (1 - ((double)CompressedSize / OriginalSize)) * 100;
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
}
