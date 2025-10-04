using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public const string MinimizeToTrayValueName = "MinimizeToTray";
        public const string RunOnStartupValueName = "RunOnStartup";
        public const string AutoShowOnD2RValueName = "AutoShowOnD2R";
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
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem trayShowMenuItem;
        private ToolStripMenuItem trayExitMenuItem;
        private bool minimizeToTray = false;
        private bool runOnStartup = false;
        private bool autoLaunchWithD2R = false;
        private bool isExiting = false;
        private bool trayBalloonShown = false;
        private bool suppressSettingEvents = false;

        // Backup System
        private BackupManager backupManager;
        private System.Threading.Timer periodicBackupTimer;
        private BackupSettings backupSettings;
        private System.Windows.Forms.Timer processMonitorTimer;
        private bool d2rProcessRunning = false;
        private DateTime? lastBackupTime;

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
        private Label lblTrayOptions;
        private FlowLayoutPanel pnlTrayOptions;
        private CheckBox chkMinimizeToTray;
        private CheckBox chkRunOnStartup;
        private CheckBox chkAutoLaunchWithD2R;

        // Language UI Controls
        private ComboBox cboLanguage;
        private Label lblLanguage;

        // UI Control References for localization
        private Label lblPath;
        private Label lblProgressText;

        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "D2RMonitor",
            "errors.log"
        );
        #endregion

        public Form1()
        {
            // Load language settings first
            LanguageManager.LoadLanguage();

            InitializeComponent();
            InitializeUI();
            InitializeTrayIcon();
            LoadSettings();
            overlayManager = new OverlayManager();
            InitializeBackupSystem();
            InitializeProcessMonitor();
            StartMonitoring();

            // Subscribe to language change event
            LanguageManager.LanguageChanged += OnLanguageChanged;
        }

        private void InitializeUI()
        {
            // 폼 설정
            Text = LanguageManager.GetString("MainTitle");
            Size = new Size(900, 780);  // Increased height for additional options
            StartPosition = FormStartPosition.CenterScreen;
            Resize += Form1_Resize;

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
                Text = LanguageManager.GetString("DebugToggleOff"),
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
                Size = new Size(870, 230),
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

            lblTrayOptions = new Label
            {
                Text = LanguageManager.GetString("TrayOptions"),
                Location = new Point(10, 160),
                AutoSize = true
            };
            grpBackup.Controls.Add(lblTrayOptions);

            pnlTrayOptions = new FlowLayoutPanel
            {
                Location = new Point(10, 182),
                Size = new Size(840, 40),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = false
            };

            chkMinimizeToTray = new CheckBox
            {
                Text = LanguageManager.GetString("ChkMinimizeToTray"),
                AutoSize = true,
                Margin = new Padding(0, 0, 25, 5)
            };
            chkMinimizeToTray.CheckedChanged += ChkMinimizeToTray_CheckedChanged;

            chkRunOnStartup = new CheckBox
            {
                Text = LanguageManager.GetString("ChkRunOnStartup"),
                AutoSize = true,
                Margin = new Padding(0, 0, 25, 5)
            };
            chkRunOnStartup.CheckedChanged += ChkRunOnStartup_CheckedChanged;

            chkAutoLaunchWithD2R = new CheckBox
            {
                Text = LanguageManager.GetString("ChkAutoLaunchWithD2R"),
                AutoSize = true,
                Margin = new Padding(0, 0, 25, 5)
            };
            chkAutoLaunchWithD2R.CheckedChanged += ChkAutoLaunchWithD2R_CheckedChanged;

            pnlTrayOptions.Controls.Add(chkMinimizeToTray);
            pnlTrayOptions.Controls.Add(chkRunOnStartup);
            pnlTrayOptions.Controls.Add(chkAutoLaunchWithD2R);
            grpBackup.Controls.Add(pnlTrayOptions);

            // 상태 레이블 (패널 아래로 이동)
            lblStatus = new Label
            {
                Text = LanguageManager.GetString("StatusMonitoring"),
                Location = new Point(10, 720),
                Size = new Size(870, 20),
                ForeColor = Color.Gray,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            Controls.Add(lblStatus);

            UpdateBackupSelectedButtonText();
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

        private void UpdateBackupSelectedButtonText()
        {
            int selectedCount = dgvFiles.SelectedRows.Count;

            if (selectedCount > 1)
            {
                btnBackupSelected.Text = string.Format(LanguageManager.GetString("BtnBackupSelectedWithCount"), selectedCount);
            }
            else
            {
                btnBackupSelected.Text = LanguageManager.GetString("BtnBackupSelected");
            }
        }

        private string GetBackupSummaryText(int successCount, int failCount)
        {
            string message = string.Format(LanguageManager.GetString("BackupSummarySuccess"), successCount);
            if (failCount > 0)
            {
                message += string.Format(LanguageManager.GetString("BackupSummaryFailureSuffix"), failCount);
            }

            return message;
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

        #region Tray and Process Integration
        private void InitializeTrayIcon()
        {
            trayMenu = new ContextMenuStrip();
            trayShowMenuItem = new ToolStripMenuItem(string.Empty, null, (_, __) => ShowFromTray());
            trayExitMenuItem = new ToolStripMenuItem(string.Empty, null, (_, __) => ExitApplication());
            trayMenu.Items.AddRange(new ToolStripItem[] { trayShowMenuItem, trayExitMenuItem });

            trayIcon = new NotifyIcon
            {
                Icon = Icon ?? SystemIcons.Application,
                Visible = false,
                ContextMenuStrip = trayMenu
            };
            trayIcon.DoubleClick += (s, e) => ShowFromTray();

            UpdateTrayLanguage();
        }

        private void InitializeProcessMonitor()
        {
            processMonitorTimer = new System.Windows.Forms.Timer
            {
                Interval = 5000
            };
            processMonitorTimer.Tick += ProcessMonitorTimer_Tick;
            processMonitorTimer.Start();
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized && minimizeToTray)
            {
                HideToTray();
            }
        }

        private void HideToTray()
        {
            if (!minimizeToTray || trayIcon == null)
            {
                return;
            }

            if (!trayIcon.Visible)
            {
                trayIcon.Visible = true;
            }

            if (!trayBalloonShown)
            {
                trayIcon.ShowBalloonTip(
                    1500,
                    LanguageManager.GetString("TrayTooltip"),
                    LanguageManager.GetString("TrayMinimized"),
                    ToolTipIcon.Info);
                trayBalloonShown = true;
            }

            Hide();
            ShowInTaskbar = false;
            UpdateTrayVisibility();
        }

        private void ShowFromTray()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Activate();
            UpdateTrayVisibility();
        }

        private void ProcessMonitorTimer_Tick(object sender, EventArgs e)
        {
            if (!autoLaunchWithD2R)
            {
                d2rProcessRunning = false;
                return;
            }

            bool isRunning = IsD2RRunning();

            if (isRunning && !d2rProcessRunning)
            {
                d2rProcessRunning = true;
                UpdateStatus(LanguageManager.GetString("StatusD2RDetected"), Color.Blue);

                if (!Visible)
                {
                    ShowFromTray();
                }

                trayIcon?.ShowBalloonTip(
                    1500,
                    LanguageManager.GetString("TrayTooltip"),
                    LanguageManager.GetString("TrayD2RDetected"),
                    ToolTipIcon.Info);
            }
            else if (!isRunning && d2rProcessRunning)
            {
                d2rProcessRunning = false;
            }
        }

        private void UpdateTrayLanguage()
        {
            if (trayShowMenuItem != null)
            {
                trayShowMenuItem.Text = LanguageManager.GetString("TrayShow");
            }

            if (trayExitMenuItem != null)
            {
                trayExitMenuItem.Text = LanguageManager.GetString("TrayExit");
            }

            if (trayIcon != null)
            {
                trayIcon.Text = LanguageManager.GetString("TrayTooltip");
            }
        }

        private void UpdateTrayVisibility()
        {
            if (trayIcon == null)
            {
                return;
            }

            bool shouldShow = minimizeToTray || runOnStartup || autoLaunchWithD2R;
            trayIcon.Visible = shouldShow || !ShowInTaskbar;
        }

        private bool IsD2RRunning()
        {
            try
            {
                return Process.GetProcessesByName("D2R").Length > 0;
            }
            catch (Exception ex)
            {
                LogError("D2R 프로세스 확인 실패", ex);
                return false;
            }
        }

        private bool TrySetStartupRegistration(bool enable)
        {
            try
            {
                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(@"Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (runKey == null)
                    {
                        throw new InvalidOperationException("Run 키에 접근할 수 없습니다.");
                    }

                    const string valueName = "D2RSaveMonitor";
                    if (enable)
                    {
                        runKey.SetValue(valueName, Application.ExecutablePath);
                    }
                    else if (runKey.GetValue(valueName) != null)
                    {
                        runKey.DeleteValue(valueName);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LanguageManager.GetString("StartupRegistrationFailed"), ex.Message),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                LogError("시작 프로그램 등록 실패", ex);
                return false;
            }
        }

        private bool IsRegisteredForStartup()
        {
            using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(@"Software\\Microsoft\\Windows\\CurrentVersion\\Run", false))
            {
                return runKey?.GetValue("D2RSaveMonitor") != null;
            }
        }

        private void ExitApplication()
        {
            isExiting = true;
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
            }
            Close();
        }

        private void ChkMinimizeToTray_CheckedChanged(object sender, EventArgs e)
        {
            if (suppressSettingEvents)
            {
                return;
            }

            minimizeToTray = chkMinimizeToTray.Checked;
            SaveSettings();
            UpdateTrayVisibility();

            if (!minimizeToTray && !Visible)
            {
                ShowFromTray();
            }
        }

        private void ChkRunOnStartup_CheckedChanged(object sender, EventArgs e)
        {
            if (suppressSettingEvents)
            {
                return;
            }

            bool requested = chkRunOnStartup.Checked;
            if (TrySetStartupRegistration(requested))
            {
                runOnStartup = requested;
                SaveSettings();
                UpdateTrayVisibility();
            }
            else
            {
                suppressSettingEvents = true;
                chkRunOnStartup.Checked = runOnStartup;
                suppressSettingEvents = false;
            }
        }

        private void ChkAutoLaunchWithD2R_CheckedChanged(object sender, EventArgs e)
        {
            if (suppressSettingEvents)
            {
                return;
            }

            autoLaunchWithD2R = chkAutoLaunchWithD2R.Checked;
            if (!autoLaunchWithD2R)
            {
                d2rProcessRunning = false;
            }

            SaveSettings();
            UpdateTrayVisibility();

            if (autoLaunchWithD2R)
            {
                ProcessMonitorTimer_Tick(this, EventArgs.Empty);
            }
        }
        #endregion


        private void OnBackupStarted(object sender, BackupEventArgs e)
        {
            // Event fired on ThreadPool, marshal to UI thread
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnBackupStarted(sender, e)));
                return;
            }

            UpdateStatus(string.Format(LanguageManager.GetString("StatusBackupInProgress"), e.FileName), Color.Blue);
        }

        private void OnBackupCompleted(object sender, BackupEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnBackupCompleted(sender, e)));
                return;
            }

            lastBackupTime = DateTime.Now;
            lblLastBackup.Text = $"{LanguageManager.GetString("LblLastBackup")}: {lastBackupTime:HH:mm:ss}";
            UpdateStatus(string.Format(LanguageManager.GetString("StatusBackupComplete"), e.FileName), Color.Green);
        }

        private void OnBackupFailed(object sender, BackupErrorEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnBackupFailed(sender, e)));
                return;
            }

            UpdateStatus(string.Format(LanguageManager.GetString("StatusBackupFailed"), e.FileName, e.ErrorMessage), Color.Red);
            LogError(string.Format(LanguageManager.GetString("StatusBackupFailed"), e.FileName, e.ErrorMessage), e.Exception);
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

                UpdateStatus(string.Format(LanguageManager.GetString("StatusMonitoringStarted"), savePath), Color.Green);
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
            string errorMessage = ex?.Message ?? LanguageManager.GetString("Unknown");
            UpdateStatus(string.Format(LanguageManager.GetString("StatusMonitoringError"), errorMessage), Color.Red);

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
                    string.Format(LanguageManager.GetString("WatcherRestartFailed"), restartEx.Message),
                    LanguageManager.GetString("WatcherRestartFailedTitle"),
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

                        object minimizeValue = key.GetValue(ConfigConstants.MinimizeToTrayValueName, 0);
                        minimizeToTray = Convert.ToInt32(minimizeValue) != 0;

                        object autoShowValue = key.GetValue(ConfigConstants.AutoShowOnD2RValueName, 0);
                        autoLaunchWithD2R = Convert.ToInt32(autoShowValue) != 0;
                    }
                    else
                    {
                        savePath = GetDefaultSavePath();
                    }
                }

                txtSavePath.Text = savePath;

                runOnStartup = IsRegisteredForStartup();

                suppressSettingEvents = true;
                chkMinimizeToTray.Checked = minimizeToTray;
                chkRunOnStartup.Checked = runOnStartup;
                chkAutoLaunchWithD2R.Checked = autoLaunchWithD2R;
                suppressSettingEvents = false;

                UpdateTrayVisibility();

                if (autoLaunchWithD2R)
                {
                    ProcessMonitorTimer_Tick(this, EventArgs.Empty);
                }
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
                        key.SetValue(ConfigConstants.MinimizeToTrayValueName, minimizeToTray ? 1 : 0);
                        key.SetValue(ConfigConstants.RunOnStartupValueName, runOnStartup ? 1 : 0);
                        key.SetValue(ConfigConstants.AutoShowOnD2RValueName, autoLaunchWithD2R ? 1 : 0);
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
                MessageBox.Show(
                    string.Format(LanguageManager.GetString("SettingsSaveFailed"), ex.Message),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
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

                if (!Directory.Exists(savePath))
                {
                    MessageBox.Show(
                        string.Format(LanguageManager.GetString("SaveFolderNotFound"), savePath),
                        LanguageManager.GetString("Error"),
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
                    string.Format(LanguageManager.GetString("OpenFolderFailed"), ex.Message),
                    LanguageManager.GetString("Error"),
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

                // 탐색기로 폴더 열기
                System.Diagnostics.Process.Start("explorer.exe", backupDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LanguageManager.GetString("OpenFolderFailed"), ex.Message),
                    LanguageManager.GetString("Error"),
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

            UpdateBackupSelectedButtonText();
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
                            string.Format(
                                LanguageManager.GetString("BackupSuccessSingle"),
                                Path.GetFileName(selectedFiles[0]),
                                Math.Round(result.Duration.TotalMilliseconds)
                            ),
                            LanguageManager.GetString("BackupSuccessSingleTitle"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }
                    else
                    {
                        MessageBox.Show(
                            string.Format(
                                LanguageManager.GetString("BackupFailureSingle"),
                                Path.GetFileName(selectedFiles[0]),
                                result.ErrorMessage
                            ),
                            LanguageManager.GetString("BackupFailureSingleTitle"),
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

                    MessageBox.Show(
                        GetBackupSummaryText(successCount, failCount),
                        LanguageManager.GetString("BackupSummarySelectedTitle"),
                        MessageBoxButtons.OK,
                        failCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LanguageManager.GetString("BackupErrorGeneric"), ex.Message),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                LogError("수동 백업 실패", ex);
            }
            finally
            {
                btnBackupSelected.Enabled = dgvFiles.SelectedRows.Count > 0;
                btnBackupAll.Enabled = true;
                UpdateBackupSelectedButtonText();
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
                        LanguageManager.GetString("NoFilesToBackup"),
                        LanguageManager.GetString("Notice"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return;
                }

                var confirmResult = MessageBox.Show(
                    string.Format(LanguageManager.GetString("ConfirmFullBackup"), files.Count),
                    LanguageManager.GetString("ConfirmFullBackupTitle"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (confirmResult != DialogResult.Yes) return;

                btnBackupSelected.Enabled = false;
                btnBackupAll.Enabled = false;

                var results = await backupManager.CreateBulkBackupAsync(files, BackupTrigger.ManualBulk);

                int successCount = results.Count(r => r.Success);
                int failCount = results.Count(r => !r.Success);

                MessageBox.Show(
                    GetBackupSummaryText(successCount, failCount),
                    LanguageManager.GetString("BackupSummaryFullTitle"),
                    MessageBoxButtons.OK,
                    failCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LanguageManager.GetString("BackupErrorGeneric"), ex.Message),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                LogError("전체 백업 실패", ex);
            }
            finally
            {
                btnBackupSelected.Enabled = dgvFiles.SelectedRows.Count > 0;
                btnBackupAll.Enabled = true;
                UpdateBackupSelectedButtonText();
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
                    string.Format(LanguageManager.GetString("OpenBackupListFailed"), ex.Message),
                    LanguageManager.GetString("Error"),
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
                    string.Format(LanguageManager.GetString("OpenSettingsFailed"), ex.Message),
                    LanguageManager.GetString("Error"),
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
                btn.Text = LanguageManager.GetString("DebugToggleOn");
                btn.BackColor = Color.OrangeRed;
                UpdateStatus(LanguageManager.GetString("DebugStatusOn"), Color.Orange);

                // Immediately show test data
                DisplayDebugData();

                // Show test overlay
                overlayManager.ShowWarning("TestCharacter.d2s", 7856);
            }
            else
            {
                btn.Text = LanguageManager.GetString("DebugToggleOff");
                btn.BackColor = Color.Gray;
                UpdateStatus(LanguageManager.GetString("DebugStatusOff"), Color.Gray);

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

            UpdateStatus(string.Format(LanguageManager.GetString("DebugTestCasesStatus"), testCases.Length), Color.Orange);
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
                    UpdateStatus(LanguageManager.GetString("StatusSaveAccessDenied"), Color.Red);
                    return;
                }
                catch (DirectoryNotFoundException)
                {
                    UpdateStatus(LanguageManager.GetString("StatusSaveNotFound"), Color.Red);
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

                string statusMessage = string.Format(
                    LanguageManager.GetString("StatusLastCheck"),
                    DateTime.Now.ToString("HH:mm:ss"),
                    successCount
                );

                if (errorCount > 0)
                {
                    statusMessage += string.Format(
                        LanguageManager.GetString("StatusErrorSuffix"),
                        errorCount
                    );
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
                UpdateStatus(LanguageManager.GetString("StatusSavePathNotSet"), Color.Red);
                return false;
            }

            try
            {
                // Check path format validity
                Path.GetFullPath(path);

                if (!Directory.Exists(path))
                {
                    UpdateStatus(string.Format(LanguageManager.GetString("StatusPathDoesNotExist"), path), Color.Red);
                    return false;
                }

                // Test read permissions
                Directory.GetFiles(path, "*.d2s", SearchOption.TopDirectoryOnly);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                UpdateStatus(LanguageManager.GetString("StatusSaveAccessDenied"), Color.Red);
                return false;
            }
            catch (PathTooLongException)
            {
                UpdateStatus(LanguageManager.GetString("StatusPathTooLong"), Color.Red);
                return false;
            }
            catch (Exception ex)
            {
                UpdateStatus(string.Format(LanguageManager.GetString("StatusPathValidationFailed"), ex.Message), Color.Red);
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

            string message = string.Format(
                LanguageManager.GetString("CriticalErrorMessage"),
                context,
                ex.Message,
                LogFilePath
            );

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
            if (!isExiting && minimizeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

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

            try
            {
                processMonitorTimer?.Stop();
                processMonitorTimer?.Dispose();
            }
            catch (Exception ex)
            {
                LogError("프로세스 모니터 타이머 정리 오류", ex);
            }

            trayIcon?.Dispose();

            base.OnFormClosing(e);
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
            btnBackupAll.Text = LanguageManager.GetString("BtnBackupAll");
            btnViewBackups.Text = LanguageManager.GetString("BtnViewBackups");
            btnBackupSettings.Text = LanguageManager.GetString("BtnBackupSettings");

            // Update backup status display
            UpdateBackupStatusDisplay();

            // Update last backup label if needed
            lblLastBackup.Text = lastBackupTime.HasValue
                ? $"{LanguageManager.GetString("LblLastBackup")}: {lastBackupTime.Value:HH:mm:ss}"
                : $"{LanguageManager.GetString("LblLastBackup")}: {LanguageManager.GetString("None")}";

            // Update backup selected button text with current selection state
            UpdateBackupSelectedButtonText();

            if (lblTrayOptions != null)
            {
                lblTrayOptions.Text = LanguageManager.GetString("TrayOptions");
            }

            if (chkMinimizeToTray != null)
            {
                chkMinimizeToTray.Text = LanguageManager.GetString("ChkMinimizeToTray");
                chkRunOnStartup.Text = LanguageManager.GetString("ChkRunOnStartup");
                chkAutoLaunchWithD2R.Text = LanguageManager.GetString("ChkAutoLaunchWithD2R");
            }

            // Status label
            lblStatus.Text = LanguageManager.GetString("StatusMonitoring");

            // Refresh the grid to update status text
            // Grid will update on next file change event
            CheckSaveFiles();

            UpdateTrayLanguage();
        }
        #endregion
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
                        CustomBackupPath = (string)key.GetValue("CustomPath", ""),
                        EnableCompression = (int)key.GetValue("EnableCompression", 1) == 1,
                        PeriodicScope = LoadPeriodicScope(key)
                    };
                }
            }
            catch
            {
                return new BackupSettings();
            }
        }

        private static PeriodicBackupScope LoadPeriodicScope(RegistryKey key)
        {
            int scopeValue = (int)key.GetValue("PeriodicScope", -1);
            if (Enum.IsDefined(typeof(PeriodicBackupScope), scopeValue))
            {
                return (PeriodicBackupScope)scopeValue;
            }

            bool includeSafeZone = (int)key.GetValue("PeriodicIncludeSafeZone", 0) == 1;
            return includeSafeZone ? PeriodicBackupScope.EntireRange : PeriodicBackupScope.WarningOrAbove;
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
                    key.SetValue("EnableCompression", EnableCompression ? 1 : 0);
                    key.SetValue("PeriodicScope", (int)PeriodicScope);
                    key.SetValue("PeriodicIncludeSafeZone", PeriodicScope == PeriodicBackupScope.EntireRange ? 1 : 0);
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

    #region Localization
    /// <summary>
    /// 지원하는 언어 / Supported Languages
    /// </summary>
    public enum Language
    {
        Korean,
        English
    }

    /// <summary>
    /// 다국어 관리 클래스 / Language Manager
    /// </summary>
    public static class LanguageManager
    {
        private static Language currentLanguage = Language.English;
        public static event EventHandler LanguageChanged;

        private const string RegistryKey = @"Software\D2RMonitor";

        public static Language CurrentLanguage
        {
            get => currentLanguage;
            set
            {
                if (currentLanguage != value)
                {
                    currentLanguage = value;
                    SaveLanguage();
                    LanguageChanged?.Invoke(null, EventArgs.Empty);
                }
            }
        }

        public static void LoadLanguage()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKey))
                {
                    if (key != null)
                    {
                        int langValue = (int)key.GetValue("Language", 1);  // Default to English (1)
                        currentLanguage = (Language)langValue;
                    }
                }
            }
            catch
            {
                currentLanguage = Language.English;  // Default to English on error
            }
        }

        private static void SaveLanguage()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKey))
                {
                    if (key != null)
                    {
                        key.SetValue("Language", (int)currentLanguage);
                    }
                }
            }
            catch
            {
                // Save failed - non-critical
            }
        }

        public static string GetString(string key)
        {
            if (currentLanguage == Language.Korean)
            {
                return Strings_KR.ContainsKey(key) ? Strings_KR[key] : key;
            }
            else
            {
                return Strings_EN.ContainsKey(key) ? Strings_EN[key] : key;
            }
        }

        // 한국어 리소스
        private static readonly Dictionary<string, string> Strings_KR = new Dictionary<string, string>
        {
            // 메인 GUI
            ["MainTitle"] = "디아블로 2 레저렉션 세이브 모니터",
            ["SavePathLabel"] = "세이브 파일 경로:",
            ["BrowseButton"] = "찾아보기...",
            ["OpenSaveFolder"] = "세이브 폴더 열기",
            ["OpenBackupFolder"] = "백업 폴더 열기",
            ["ColumnFileName"] = "캐릭터 파일명",
            ["ColumnCurrentSize"] = "현재 크기",
            ["ColumnPercentage"] = "비율",
            ["ColumnMaxSize"] = "최대 크기",
            ["ColumnStatus"] = "상태",
            ["SelectedFiles"] = "선택된 파일",
            ["BackupSelected"] = "선택 백업",
            ["RestoreBackup"] = "백업 복원",
            ["BackupSettings"] = "백업 설정",
            ["StatusSafe"] = "안전",
            ["StatusWarning"] = "경고",
            ["StatusDanger"] = "위험",

            // 백업 설정
            ["BackupSettingsTitle"] = "백업 설정",
            ["AutoBackupSettings"] = "자동 백업 설정:",
            ["AutoBackupDanger"] = "위험 수준(7500 bytes) 도달 시 자동 백업",
            ["EnableCompression"] = "백업 파일 압축 (디스크 공간 50~70% 절약)",
            ["PeriodicBackupSettings"] = "주기적 백업 설정:",
            ["PeriodicBackupEnable"] = "주기적 자동 백업 활성화",
            ["PeriodicScopeGroup"] = "백업 대상 범위",
            ["PeriodicScopeDangerDetailed"] = "위험 구간만 (7500 bytes 이상)",
            ["PeriodicScopeWarningDetailed"] = "경고 이상 (7000 bytes 이상)",
            ["PeriodicScopeAllDetailed"] = "전체 구간 (변경 발생 시마다)",
            ["PeriodicScopeHint"] = "* 주기 백업 활성화 시 선택된 조건을 만족하는 파일을 백업합니다.",
            ["TrayOptions"] = "트레이 및 자동 실행 옵션",
            ["BackupIntervalMin"] = "백업 주기(분):",
            ["Minutes"] = "분",
            ["BackupRetentionSettings"] = "백업 보관 설정:",
            ["MaxBackupsPerFile"] = "파일당 최대 백업 개수:",
            ["BackupCooldownSec"] = "자동 백업 쿨다운(초):",
            ["Seconds"] = "초",
            ["Count"] = "개",
            ["ResetDefaults"] = "기본값으로 복원",
            ["Cancel"] = "취소",
            ["Save"] = "저장",
            ["SaveSuccess"] = "설정이 저장되었습니다.\n\n참고: 압축 설정은 새로 생성되는 백업부터 적용됩니다.",
            ["SaveSuccessTitle"] = "저장 완료",
            ["ResetConfirm"] = "모든 설정을 기본값으로 복원하시겠습니까?",
            ["ResetConfirmTitle"] = "기본값 복원",

            // 백업 복원
            ["BackupHistoryTitle"] = "백업 목록 및 복원",
            ["CharacterFilter"] = "캐릭터 필터:",
            ["AllCharacters"] = "모든 캐릭터",
            ["Refresh"] = "새로고침",
            ["Close"] = "닫기",
            ["ColumnBackupTime"] = "백업 시간",
            ["ColumnCharacter"] = "캐릭터",
            ["ColumnFileSize"] = "파일 크기",
            ["ColumnCompression"] = "압축",
            ["ColumnTrigger"] = "백업 원인",
            ["ColumnType"] = "유형",
            ["Restore"] = "복원",
            ["Delete"] = "삭제",
            ["BackupInfoPlaceholder"] = "백업을 선택하면 상세 정보가 표시됩니다.",
            ["SelectedBackup"] = "선택된 백업:",
            ["SelectedBackupInfo"] = "선택된 백업:\n캐릭터: {0}\n백업 파일: {1}\n크기: {2} bytes ({3:F1}%)",
            ["SelectedBackupCount"] = "선택된 백업: {0}개",
            ["Compressed"] = "압축",
            ["Automatic"] = "자동",
            ["Manual"] = "수동",
            ["RestoreWithCount"] = "복원 ({0}개)",
            ["DeleteWithCount"] = "삭제 ({0}개)",

            // 메시지
            ["SelectSaveFolder"] = "D2R 세이브 파일 폴더를 선택하세요",
            ["LoadingBackups"] = "백업 목록 로딩 중...",
            ["BackupLoadFailed"] = "백업 목록 로드 실패:\n{0}",
            ["RestoreOnlyOne"] = "복원은 한 번에 하나의 백업만 가능합니다.\n첫 번째 선택된 백업만 복원됩니다.",
            ["Notice"] = "알림",
            ["RestoreConfirm"] = "다음 백업으로 복원하시겠습니까?\n\n캐릭터: {0}\n백업 시간: {1}\n파일 크기: {2} bytes\n\n현재 파일은 자동으로 백업됩니다.",
            ["RestoreConfirmTitle"] = "백업 복원 확인",
            ["RestoreSuccess"] = "복원 완료: {0}\n\n현재 파일은 다음으로 백업되었습니다:\n{1}",
            ["RestoreSuccessTitle"] = "복원 성공",
            ["RestoreFailed"] = "복원 실패: {0}",
            ["RestoreFailedTitle"] = "복원 실패",
            ["Error"] = "오류",
            ["DeleteConfirm"] = "다음 백업을 삭제하시겠습니까?\n\n캐릭터: {0}\n백업 시간: {1}\n\n이 작업은 되돌릴 수 없습니다.",
            ["DeleteMultipleConfirm"] = "{0}개의 백업을 삭제하시겠습니까?\n\n이 작업은 되돌릴 수 없습니다.",
            ["DeleteConfirmTitle"] = "백업 삭제 확인",
            ["DeleteSuccess"] = "백업이 삭제되었습니다.",
            ["DeleteSuccessTitle"] = "삭제 완료",
            ["DeleteFailed"] = "백업 삭제에 실패했습니다.",
            ["DeletePartialSuccess"] = "삭제 완료: {0}개 성공, {1}개 실패",
            ["DeleteMultipleSuccess"] = "삭제 완료: {0}개 백업 삭제",
            ["DeletePartialTitle"] = "삭제 완료 (일부 실패)",
            ["DeleteFailedTitle"] = "삭제 실패",
            ["DeleteFailedWithReason"] = "백업 삭제에 실패했습니다.\n오류: {0}",
            ["RefreshFailed"] = "새로고침 중 오류 발생:\n{0}",
            ["StartupRegistrationFailed"] = "시작 프로그램 등록에 실패했습니다:\n{0}",
            ["TrayShow"] = "창 열기",
            ["TrayExit"] = "종료",
            ["TrayTooltip"] = "D2R 세이브 모니터",
            ["TrayMinimized"] = "D2R 세이브 모니터가 트레이에서 실행 중입니다.",
            ["TrayD2RDetected"] = "D2R 실행을 감지했습니다.",
            ["WatcherRestartFailed"] = "파일 모니터링 재시작 실패:\n{0}\n\n애플리케이션을 다시 시작하세요.",
            ["WatcherRestartFailedTitle"] = "심각한 오류",
            ["SettingsSaveFailed"] = "설정 저장 실패:\n{0}",
            ["SavePathNotSet"] = "세이브 경로가 설정되지 않았습니다.",
            ["SaveFolderNotFound"] = "세이브 폴더를 찾을 수 없습니다:\n{0}",
            ["OpenFolderFailed"] = "폴더를 열 수 없습니다:\n{0}",
            ["BackupSystemNotInitialized"] = "백업 시스템이 초기화되지 않았습니다.",
            ["BackupSuccessSingle"] = "백업 완료: {0}\n백업 시간: {1} ms",
            ["BackupSuccessSingleTitle"] = "백업 성공",
            ["BackupFailureSingle"] = "백업 실패: {0}\n오류: {1}",
            ["BackupFailureSingleTitle"] = "백업 실패",
            ["ConfirmSelectedBackup"] = "{0}개의 파일을 백업하시겠습니까?",
            ["ConfirmSelectedBackupTitle"] = "선택 파일 백업 확인",
            ["BackupSummarySuccess"] = "백업 완료: {0}개 성공",
            ["BackupSummaryFailureSuffix"] = ", {0}개 실패",
            ["BackupSummarySelectedTitle"] = "선택 파일 백업 완료",
            ["BackupSummaryFullTitle"] = "전체 백업 완료",
            ["BackupErrorGeneric"] = "백업 중 오류 발생:\n{0}",
            ["NoFilesToBackup"] = "백업할 파일이 없습니다.",
            ["ConfirmFullBackup"] = "{0}개의 캐릭터 파일을 백업하시겠습니까?",
            ["ConfirmFullBackupTitle"] = "전체 백업 확인",
            ["OpenBackupListFailed"] = "백업 목록을 열 수 없습니다:\n{0}",
            ["OpenSettingsFailed"] = "설정을 열 수 없습니다:\n{0}",
            ["DebugToggleOn"] = "디버그: ON",
            ["DebugToggleOff"] = "디버그: OFF",
            ["DebugStatusOn"] = "디버그 모드 활성화 - 테스트 데이터 표시",
            ["DebugStatusOff"] = "디버그 모드 비활성화",
            ["DebugTestCasesStatus"] = "디버그 모드: {0}개 테스트 케이스",
            ["StatusBackupInProgress"] = "백업 중: {0}",
            ["StatusBackupComplete"] = "백업 완료: {0}",
            ["StatusBackupFailed"] = "백업 실패: {0} - {1}",
            ["StatusMonitoringStarted"] = "모니터링 시작: {0}",
            ["StatusMonitoringError"] = "모니터링 오류: {0}",
            ["StatusSaveAccessDenied"] = "세이브 폴더 접근 권한 없음",
            ["StatusSaveNotFound"] = "세이브 폴더를 찾을 수 없음",
            ["StatusSavePathNotSet"] = "세이브 경로가 설정되지 않았습니다",
            ["StatusPathDoesNotExist"] = "경로가 존재하지 않습니다: {0}",
            ["StatusPathTooLong"] = "경로가 너무 깁니다",
            ["StatusPathValidationFailed"] = "경로 검증 실패: {0}",
            ["StatusLastCheck"] = "마지막 체크: {0} - {1}개 파일 처리됨",
            ["StatusErrorSuffix"] = " ({0}개 오류)",
            ["CriticalErrorMessage"] = "{0}\n\n오류: {1}\n\n로그 위치: {2}\n\n계속 진행하시겠습니까?",
            ["CriticalErrorTitle"] = "심각한 오류",

            // 백업 트리거
            ["TriggerDanger"] = "위험 수준 도달",
            ["TriggerPeriodic"] = "주기적 자동",
            ["TriggerManual"] = "수동",
            ["TriggerManualSingle"] = "수동 백업 (단일)",
            ["TriggerManualBulk"] = "수동 백업 (전체)",
            ["TriggerPreRestore"] = "복원 전",
            ["CompressionRatioFormat"] = "압축 {0:F0}%",
            ["CompressionNotAvailable"] = "-",
            ["NoBackups"] = "백업이 없습니다.",
            ["Unknown"] = "알 수 없음",

            // 언어 설정
            ["Language"] = "언어 / Language",
            ["LanguageSettings"] = "언어 설정:",
            ["Korean"] = "한국어",
            ["English"] = "English",

            // Form1 UI 추가 strings
            ["BtnBrowse"] = "찾아보기...",
            ["BtnOpenSaveFolder"] = "📁 세이브",
            ["BtnOpenBackupFolder"] = "📁 백업",
            ["TipOpenSaveFolder"] = "세이브 폴더 열기",
            ["TipOpenBackupFolder"] = "백업 폴더 열기",
            ["ColFileName"] = "캐릭터 파일명",
            ["ColCurrentSize"] = "현재 크기",
            ["ColLimit"] = "제한",
            ["ColPercentage"] = "사용률",
            ["ColProgressBar"] = "진행 상태",
            ["ColStatus"] = "상태",
            ["GrpBackup"] = "백업 관리",
            ["BtnBackupSelected"] = "선택 파일 백업",
            ["BtnBackupAll"] = "전체 백업",
            ["BtnViewBackups"] = "백업 복원...",
            ["BtnBackupSettings"] = "백업 설정...",
            ["ChkMinimizeToTray"] = "트레이로 최소화",
            ["ChkRunOnStartup"] = "Windows 시작 시 실행",
            ["ChkAutoLaunchWithD2R"] = "D2R 실행 시 자동 표시",
            ["BtnBackupSelectedWithCount"] = "선택 파일 백업 ({0}개)",
            ["LblLastBackup"] = "마지막 백업",
            ["None"] = "없음",
            ["AutoBackup"] = "자동 백업",
            ["AutoBackupDangerOn"] = "위험 수준 도달 시 자동 백업: 사용",
            ["AutoBackupDangerOff"] = "위험 수준 자동 백업: 사용 안 함",
            ["PeriodicBackup"] = "주기적 백업",
            ["PeriodicBackupOnSummary"] = "주기 백업: {0} 간격 | 범위: {1}",
            ["PeriodicBackupOffSummary"] = "주기 백업: 사용 안 함",
            ["BackupLocationInfo"] = "백업 위치: {0}",
            ["PeriodicRangeDanger"] = "위험 구간",
            ["PeriodicRangeWarning"] = "경고 이상",
            ["PeriodicRangeAll"] = "전체 구간",
            ["Enabled"] = "활성화",
            ["Disabled"] = "비활성화",
            ["StatusMonitoring"] = "모니터링 대기 중...",
            ["StatusD2RDetected"] = "D2R 실행 감지 - 모니터링 시작",
            ["BackupInitFailed"] = "백업 시스템 초기화 실패",
            ["MonitoringContinues"] = "모니터링은 계속됩니다",
            ["Warning"] = "경고"
        };

        // 영어 리소스
        private static readonly Dictionary<string, string> Strings_EN = new Dictionary<string, string>
        {
            // Main GUI
            ["MainTitle"] = "Diablo 2 Resurrected Save Monitor",
            ["SavePathLabel"] = "Save File Path:",
            ["BrowseButton"] = "Browse...",
            ["OpenSaveFolder"] = "Open Save Folder",
            ["OpenBackupFolder"] = "Open Backup Folder",
            ["ColumnFileName"] = "Character File",
            ["ColumnCurrentSize"] = "Current Size",
            ["ColumnPercentage"] = "Percentage",
            ["ColumnMaxSize"] = "Max Size",
            ["ColumnStatus"] = "Status",
            ["SelectedFiles"] = "Selected Files",
            ["BackupSelected"] = "Backup Selected",
            ["RestoreBackup"] = "Restore Backup",
            ["BackupSettings"] = "Backup Settings",
            ["StatusSafe"] = "Safe",
            ["StatusWarning"] = "Warning",
            ["StatusDanger"] = "Danger",

            // Backup Settings
            ["BackupSettingsTitle"] = "Backup Settings",
            ["AutoBackupSettings"] = "Auto Backup Settings:",
            ["AutoBackupDanger"] = "Auto-backup at danger level (7500 bytes)",
            ["EnableCompression"] = "Enable backup compression (50~70% disk space savings)",
            ["PeriodicBackupSettings"] = "Periodic Backup Settings:",
            ["PeriodicBackupEnable"] = "Enable periodic auto-backup",
            ["PeriodicScopeGroup"] = "Backup Scope",
            ["PeriodicScopeDangerDetailed"] = "Danger zone only (>= 7500 bytes)",
            ["PeriodicScopeWarningDetailed"] = "Warning or higher (>= 7000 bytes)",
            ["PeriodicScopeAllDetailed"] = "Entire range (on every change)",
            ["PeriodicScopeHint"] = "* When periodic backup is enabled, files matching the selected criteria will be backed up.",
            ["TrayOptions"] = "Tray & startup options",
            ["BackupIntervalMin"] = "Backup interval (min):",
            ["Minutes"] = "min",
            ["BackupRetentionSettings"] = "Backup Retention Settings:",
            ["MaxBackupsPerFile"] = "Max backups per file:",
            ["BackupCooldownSec"] = "Auto-backup cooldown (sec):",
            ["Seconds"] = "sec",
            ["Count"] = "count",
            ["ResetDefaults"] = "Reset to Defaults",
            ["Cancel"] = "Cancel",
            ["Save"] = "Save",
            ["SaveSuccess"] = "Settings saved successfully.\n\nNote: Compression settings apply to new backups only.",
            ["SaveSuccessTitle"] = "Save Successful",
            ["ResetConfirm"] = "Reset all settings to defaults?",
            ["ResetConfirmTitle"] = "Reset Defaults",

            // Backup History
            ["BackupHistoryTitle"] = "Backup History and Restore",
            ["CharacterFilter"] = "Character Filter:",
            ["AllCharacters"] = "All Characters",
            ["Refresh"] = "Refresh",
            ["Close"] = "Close",
            ["ColumnBackupTime"] = "Backup Time",
            ["ColumnCharacter"] = "Character",
            ["ColumnFileSize"] = "File Size",
            ["ColumnCompression"] = "Compression",
            ["ColumnTrigger"] = "Trigger",
            ["ColumnType"] = "Type",
            ["Restore"] = "Restore",
            ["Delete"] = "Delete",
            ["BackupInfoPlaceholder"] = "Select a backup to view details.",
            ["SelectedBackup"] = "Selected backup:",
            ["SelectedBackupInfo"] = "Selected backup:\nCharacter: {0}\nBackup file: {1}\nSize: {2} bytes ({3:F1}%)",
            ["SelectedBackupCount"] = "Selected backups: {0}",
            ["Compressed"] = "Compressed",
            ["Automatic"] = "Auto",
            ["Manual"] = "Manual",
            ["RestoreWithCount"] = "Restore ({0})",
            ["DeleteWithCount"] = "Delete ({0})",

            // Messages
            ["SelectSaveFolder"] = "Select D2R save file folder",
            ["LoadingBackups"] = "Loading backups...",
            ["BackupLoadFailed"] = "Failed to load backup list:\n{0}",
            ["RestoreOnlyOne"] = "Only one backup can be restored at a time.\nOnly the first selected backup will be restored.",
            ["Notice"] = "Notice",
            ["RestoreConfirm"] = "Restore from this backup?\n\nCharacter: {0}\nBackup time: {1}\nFile size: {2} bytes\n\nCurrent file will be backed up automatically.",
            ["RestoreConfirmTitle"] = "Confirm Restore",
            ["RestoreSuccess"] = "Restore complete: {0}\n\nCurrent file backed up as:\n{1}",
            ["RestoreSuccessTitle"] = "Restore Successful",
            ["RestoreFailed"] = "Restore failed: {0}",
            ["RestoreFailedTitle"] = "Restore Failed",
            ["Error"] = "Error",
            ["DeleteConfirm"] = "Delete this backup?\n\nCharacter: {0}\nBackup time: {1}\n\nThis action cannot be undone.",
            ["DeleteMultipleConfirm"] = "Delete {0} backups?\n\nThis action cannot be undone.",
            ["DeleteConfirmTitle"] = "Confirm Delete",
            ["DeleteSuccess"] = "Backup deleted successfully.",
            ["DeleteSuccessTitle"] = "Delete Successful",
            ["DeleteFailed"] = "Failed to delete backup.",
            ["DeletePartialSuccess"] = "Delete complete: {0} succeeded, {1} failed",
            ["DeleteMultipleSuccess"] = "Delete complete: {0} backups removed",
            ["DeletePartialTitle"] = "Delete Complete (Partial)",
            ["DeleteFailedTitle"] = "Delete Failed",
            ["DeleteFailedWithReason"] = "Failed to delete backup.\nError: {0}",
            ["RefreshFailed"] = "Error while refreshing:\n{0}",
            ["StartupRegistrationFailed"] = "Failed to update startup registration:\n{0}",
            ["TrayShow"] = "Open Window",
            ["TrayExit"] = "Exit",
            ["TrayTooltip"] = "D2R Save Monitor",
            ["TrayMinimized"] = "D2R Save Monitor continues running in the tray.",
            ["TrayD2RDetected"] = "Detected D2R launch.",
            ["WatcherRestartFailed"] = "Failed to restart file monitoring:\n{0}\n\nPlease restart the application.",
            ["WatcherRestartFailedTitle"] = "Critical Error",
            ["SettingsSaveFailed"] = "Failed to save settings:\n{0}",
            ["SavePathNotSet"] = "Save path is not configured.",
            ["SaveFolderNotFound"] = "Cannot find save folder:\n{0}",
            ["OpenFolderFailed"] = "Unable to open folder:\n{0}",
            ["BackupSystemNotInitialized"] = "Backup system is not initialized.",
            ["BackupSuccessSingle"] = "Backup complete: {0}\nElapsed: {1} ms",
            ["BackupSuccessSingleTitle"] = "Backup Complete",
            ["BackupFailureSingle"] = "Backup failed: {0}\nError: {1}",
            ["BackupFailureSingleTitle"] = "Backup Failed",
            ["ConfirmSelectedBackup"] = "Backup {0} selected files?",
            ["ConfirmSelectedBackupTitle"] = "Confirm Selected Backup",
            ["BackupSummarySuccess"] = "Backup finished: {0} succeeded",
            ["BackupSummaryFailureSuffix"] = ", {0} failed",
            ["BackupSummarySelectedTitle"] = "Selected Backup Result",
            ["BackupSummaryFullTitle"] = "Full Backup Result",
            ["BackupErrorGeneric"] = "Backup error:\n{0}",
            ["NoFilesToBackup"] = "No files to backup.",
            ["ConfirmFullBackup"] = "Backup all {0} save files?",
            ["ConfirmFullBackupTitle"] = "Confirm Full Backup",
            ["OpenBackupListFailed"] = "Unable to open backup history:\n{0}",
            ["OpenSettingsFailed"] = "Unable to open settings:\n{0}",
            ["DebugToggleOn"] = "Debug: ON",
            ["DebugToggleOff"] = "Debug: OFF",
            ["DebugStatusOn"] = "Debug mode enabled - showing test data",
            ["DebugStatusOff"] = "Debug mode disabled",
            ["DebugTestCasesStatus"] = "Debug mode: {0} test cases",
            ["StatusBackupInProgress"] = "Backing up: {0}",
            ["StatusBackupComplete"] = "Backup complete: {0}",
            ["StatusBackupFailed"] = "Backup failed: {0} - {1}",
            ["StatusMonitoringStarted"] = "Monitoring started: {0}",
            ["StatusMonitoringError"] = "Monitoring error: {0}",
            ["StatusSaveAccessDenied"] = "Access denied to save folder",
            ["StatusSaveNotFound"] = "Save folder not found",
            ["StatusSavePathNotSet"] = "Save path is not configured",
            ["StatusPathDoesNotExist"] = "Path does not exist: {0}",
            ["StatusPathTooLong"] = "Path is too long",
            ["StatusPathValidationFailed"] = "Path validation failed: {0}",
            ["StatusLastCheck"] = "Last check: {0} - processed {1} files",
            ["StatusErrorSuffix"] = " ({0} errors)",
            ["CriticalErrorMessage"] = "{0}\n\nError: {1}\n\nLog location: {2}\n\nContinue?",
            ["CriticalErrorTitle"] = "Critical Error",

            // Backup Triggers
            ["TriggerDanger"] = "Danger Level",
            ["TriggerPeriodic"] = "Periodic Auto",
            ["TriggerManual"] = "Manual",
            ["TriggerManualSingle"] = "Manual backup (single)",
            ["TriggerManualBulk"] = "Manual backup (bulk)",
            ["TriggerPreRestore"] = "Pre-Restore",
            ["CompressionRatioFormat"] = "Compressed {0:F0}%",
            ["CompressionNotAvailable"] = "-",
            ["NoBackups"] = "No backups found.",
            ["Unknown"] = "Unknown",

            // Language Settings
            ["Language"] = "Language / 언어",
            ["LanguageSettings"] = "Language Settings:",
            ["Korean"] = "한국어",
            ["English"] = "English",

            // Form1 UI additional strings
            ["BtnBrowse"] = "Browse...",
            ["BtnOpenSaveFolder"] = "📁 Save",
            ["BtnOpenBackupFolder"] = "📁 Backup",
            ["TipOpenSaveFolder"] = "Open save folder",
            ["TipOpenBackupFolder"] = "Open backup folder",
            ["ColFileName"] = "Character File",
            ["ColCurrentSize"] = "Current Size",
            ["ColLimit"] = "Limit",
            ["ColPercentage"] = "Usage",
            ["ColProgressBar"] = "Progress",
            ["ColStatus"] = "Status",
            ["GrpBackup"] = "Backup Management",
            ["BtnBackupSelected"] = "Backup Selected",
            ["BtnBackupAll"] = "Backup All",
            ["BtnViewBackups"] = "Restore Backup...",
            ["BtnBackupSettings"] = "Backup Settings...",
            ["ChkMinimizeToTray"] = "Minimize to tray",
            ["ChkRunOnStartup"] = "Run at Windows startup",
            ["ChkAutoLaunchWithD2R"] = "Show when D2R starts",
            ["BtnBackupSelectedWithCount"] = "Backup Selected ({0})",
            ["LblLastBackup"] = "Last backup",
            ["None"] = "None",
            ["AutoBackup"] = "Auto backup",
            ["AutoBackupDangerOn"] = "Auto-backup at danger threshold: Enabled",
            ["AutoBackupDangerOff"] = "Auto-backup at danger threshold: Disabled",
            ["PeriodicBackup"] = "Periodic backup",
            ["PeriodicBackupOnSummary"] = "Periodic backup: every {0} | Scope: {1}",
            ["PeriodicBackupOffSummary"] = "Periodic backup: Disabled",
            ["BackupLocationInfo"] = "Backup location: {0}",
            ["PeriodicRangeDanger"] = "Danger only",
            ["PeriodicRangeWarning"] = "Warning or higher",
            ["PeriodicRangeAll"] = "Entire range",
            ["Enabled"] = "Enabled",
            ["Disabled"] = "Disabled",
            ["StatusMonitoring"] = "Monitoring...",
            ["StatusD2RDetected"] = "D2R detected - monitoring active",
            ["BackupInitFailed"] = "Backup system initialization failed",
            ["MonitoringContinues"] = "Monitoring will continue",
            ["Warning"] = "Warning"
        };
    }
    #endregion
}
