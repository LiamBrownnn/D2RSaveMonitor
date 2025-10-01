using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

namespace D2RSaveMonitor
{
    /// <summary>
    /// Backup history and restore dialog
    /// </summary>
    public class BackupHistoryForm : Form
    {
        private readonly BackupManager backupManager;
        private readonly string savePath;
        private bool isLoading = false;

        private ComboBox cmbCharacterFilter;
        private DataGridView dgvBackups;
        private Button btnRestore;
        private Button btnDelete;
        private Button btnRefresh;
        private Button btnClose;
        private Label lblBackupInfo;

        public BackupHistoryForm(BackupManager manager, string saveDirectory)
        {
            backupManager = manager ?? throw new ArgumentNullException(nameof(manager));
            savePath = saveDirectory;
            InitializeComponent();

            // Use Shown event instead of Load to prevent deadlock
            this.Shown += OnFormShown;
        }

        private async void OnFormShown(object sender, EventArgs e)
        {
            // Unhook to prevent multiple calls
            this.Shown -= OnFormShown;

            try
            {
                await LoadBackupsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"백업 목록 로드 실패:\n{ex.Message}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private void InitializeComponent()
        {
            Text = "백업 목록 및 복원";
            Size = new Size(900, 600);
            StartPosition = FormStartPosition.CenterParent;

            // Character filter
            Label lblFilter = new Label
            {
                Text = "캐릭터 필터:",
                Location = new Point(20, 20),
                Size = new Size(80, 20)
            };
            Controls.Add(lblFilter);

            cmbCharacterFilter = new ComboBox
            {
                Location = new Point(100, 18),
                Size = new Size(200, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbCharacterFilter.SelectedIndexChanged += CmbCharacterFilter_SelectedIndexChanged;
            Controls.Add(cmbCharacterFilter);

            // Refresh button
            btnRefresh = new Button
            {
                Text = "새로고침",
                Location = new Point(320, 17),
                Size = new Size(100, 28)
            };
            btnRefresh.Click += BtnRefresh_Click;
            Controls.Add(btnRefresh);

            // Backups grid
            dgvBackups = new DataGridView
            {
                Location = new Point(20, 60),
                Size = new Size(850, 400),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            dgvBackups.SelectionChanged += DgvBackups_SelectionChanged;
            Controls.Add(dgvBackups);

            // Setup columns
            dgvBackups.Columns.Add("Timestamp", "백업 시간");
            dgvBackups.Columns.Add("Character", "캐릭터");
            dgvBackups.Columns.Add("Size", "파일 크기");
            dgvBackups.Columns.Add("Trigger", "백업 원인");
            dgvBackups.Columns.Add("Type", "유형");

            dgvBackups.Columns["Timestamp"].Width = 180;
            dgvBackups.Columns["Character"].Width = 180;
            dgvBackups.Columns["Size"].Width = 120;
            dgvBackups.Columns["Trigger"].Width = 150;
            dgvBackups.Columns["Type"].Width = 100;

            // Backup info label
            lblBackupInfo = new Label
            {
                Text = "백업을 선택하면 상세 정보가 표시됩니다.",
                Location = new Point(20, 470),
                Size = new Size(850, 40),
                ForeColor = Color.Gray
            };
            Controls.Add(lblBackupInfo);

            // Buttons
            btnRestore = new Button
            {
                Text = "복원",
                Location = new Point(550, 520),
                Size = new Size(100, 30),
                Enabled = false
            };
            btnRestore.Click += BtnRestore_Click;
            Controls.Add(btnRestore);

            btnDelete = new Button
            {
                Text = "삭제",
                Location = new Point(660, 520),
                Size = new Size(100, 30),
                Enabled = false
            };
            btnDelete.Click += BtnDelete_Click;
            Controls.Add(btnDelete);

            btnClose = new Button
            {
                Text = "닫기",
                Location = new Point(770, 520),
                Size = new Size(100, 30),
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(btnClose);

            CancelButton = btnClose;
        }

        private async Task LoadBackupsAsync(string characterFilter = null)
        {
            // Prevent re-entrance
            if (isLoading) return;

            try
            {
                isLoading = true;

                // Show loading state
                dgvBackups.Enabled = false;
                cmbCharacterFilter.Enabled = false;
                btnRefresh.Enabled = false;
                lblBackupInfo.Text = "백업 목록 로딩 중...";
                lblBackupInfo.ForeColor = Color.Gray;

                // Load backups in background thread
                var allBackups = await Task.Run(() => backupManager.GetAllBackups());

                // Update UI on main thread
                UpdateUI(allBackups, characterFilter);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"백업 목록 로드 실패:\n{ex.Message}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                isLoading = false;
                dgvBackups.Enabled = true;
                cmbCharacterFilter.Enabled = true;
                btnRefresh.Enabled = true;
            }
        }

        private void UpdateUI(List<BackupMetadata> allBackups, string characterFilter)
        {
            dgvBackups.SuspendLayout();
            try
            {
                dgvBackups.Rows.Clear();

                // Group by character for filter
                var characters = allBackups
                    .Select(b => b.OriginalFile)
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();

                // Update character filter dropdown without triggering events
                cmbCharacterFilter.SelectedIndexChanged -= CmbCharacterFilter_SelectedIndexChanged;
                try
                {
                    cmbCharacterFilter.Items.Clear();
                    cmbCharacterFilter.Items.Add("모든 캐릭터");
                    foreach (var character in characters)
                    {
                        cmbCharacterFilter.Items.Add(character);
                    }

                    if (cmbCharacterFilter.SelectedIndex < 0)
                    {
                        cmbCharacterFilter.SelectedIndex = 0;
                    }
                }
                finally
                {
                    cmbCharacterFilter.SelectedIndexChanged += CmbCharacterFilter_SelectedIndexChanged;
                }

                // Apply filter
                var filteredBackups = string.IsNullOrEmpty(characterFilter)
                    ? allBackups
                    : allBackups.Where(b => b.OriginalFile == characterFilter).ToList();

                // Add to grid
                foreach (var backup in filteredBackups)
                {
                    string trigger = GetTriggerDisplayName(backup.TriggerReason);
                    string type = backup.IsAutomatic ? "자동" : "수동";

                    dgvBackups.Rows.Add(
                        backup.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                        Path.GetFileNameWithoutExtension(backup.OriginalFile),
                        $"{backup.FileSize} bytes",
                        trigger,
                        type
                    );

                    // Store metadata in row tag
                    dgvBackups.Rows[dgvBackups.Rows.Count - 1].Tag = backup;
                }

                lblBackupInfo.Text = dgvBackups.Rows.Count == 0
                    ? "백업이 없습니다."
                    : "백업을 선택하면 상세 정보가 표시됩니다.";
                lblBackupInfo.ForeColor = Color.Gray;
            }
            finally
            {
                dgvBackups.ResumeLayout();
            }
        }

        private string GetTriggerDisplayName(BackupTrigger trigger)
        {
            switch (trigger)
            {
                case BackupTrigger.DangerThreshold:
                    return "위험 임계값 도달";
                case BackupTrigger.PeriodicAutomatic:
                    return "주기적 자동 백업";
                case BackupTrigger.ManualSingle:
                    return "수동 백업 (단일)";
                case BackupTrigger.ManualBulk:
                    return "수동 백업 (전체)";
                case BackupTrigger.PreRestore:
                    return "복원 전 백업";
                default:
                    return "알 수 없음";
            }
        }

        private async void CmbCharacterFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbCharacterFilter.SelectedIndex <= 0)
            {
                await LoadBackupsAsync(); // All characters
            }
            else
            {
                string character = cmbCharacterFilter.SelectedItem.ToString();
                await LoadBackupsAsync(character);
            }
        }

        private void DgvBackups_SelectionChanged(object sender, EventArgs e)
        {
            if (dgvBackups.SelectedRows.Count > 0)
            {
                btnRestore.Enabled = true;
                btnDelete.Enabled = true;

                var backup = dgvBackups.SelectedRows[0].Tag as BackupMetadata;
                if (backup != null)
                {
                    lblBackupInfo.Text = $"선택된 백업:\n" +
                                        $"캐릭터: {backup.OriginalFile}\n" +
                                        $"백업 파일: {backup.BackupFile}\n" +
                                        $"크기: {backup.FileSize} bytes ({(double)backup.FileSize / FileConstants.MaxFileSize * 100:F1}%)";
                }
            }
            else
            {
                btnRestore.Enabled = false;
                btnDelete.Enabled = false;
                lblBackupInfo.Text = "백업을 선택하면 상세 정보가 표시됩니다.";
            }
        }

        private async void BtnRestore_Click(object sender, EventArgs e)
        {
            if (dgvBackups.SelectedRows.Count == 0) return;

            var backup = dgvBackups.SelectedRows[0].Tag as BackupMetadata;
            if (backup == null) return;

            try
            {
                var confirmResult = MessageBox.Show(
                    $"다음 백업으로 복원하시겠습니까?\n\n" +
                    $"캐릭터: {backup.OriginalFile}\n" +
                    $"백업 시간: {backup.Timestamp:yyyy-MM-dd HH:mm:ss}\n" +
                    $"파일 크기: {backup.FileSize} bytes\n\n" +
                    $"현재 파일은 자동으로 백업됩니다.",
                    "백업 복원 확인",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (confirmResult != DialogResult.Yes) return;

                btnRestore.Enabled = false;
                btnDelete.Enabled = false;
                btnRefresh.Enabled = false;

                string targetPath = Path.Combine(savePath, backup.OriginalFile);
                var result = await backupManager.RestoreBackupAsync(backup, targetPath, createPreRestoreBackup: true);

                if (result.Success)
                {
                    MessageBox.Show(
                        $"복원 완료: {backup.OriginalFile}\n\n" +
                        $"현재 파일은 다음으로 백업되었습니다:\n{result.PreRestoreBackup?.BackupFile}",
                        "복원 성공",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );

                    DialogResult = DialogResult.OK; // Signal to refresh main form
                    Close();
                }
                else
                {
                    MessageBox.Show(
                        $"복원 실패: {result.ErrorMessage}",
                        "복원 실패",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"복원 중 오류 발생:\n{ex.Message}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                btnRestore.Enabled = dgvBackups.SelectedRows.Count > 0;
                btnDelete.Enabled = dgvBackups.SelectedRows.Count > 0;
                btnRefresh.Enabled = true;
            }
        }

        private async void BtnDelete_Click(object sender, EventArgs e)
        {
            if (dgvBackups.SelectedRows.Count == 0) return;

            var backup = dgvBackups.SelectedRows[0].Tag as BackupMetadata;
            if (backup == null) return;

            try
            {
                var confirmResult = MessageBox.Show(
                    $"다음 백업을 삭제하시겠습니까?\n\n" +
                    $"캐릭터: {backup.OriginalFile}\n" +
                    $"백업 시간: {backup.Timestamp:yyyy-MM-dd HH:mm:ss}\n\n" +
                    $"이 작업은 되돌릴 수 없습니다.",
                    "백업 삭제 확인",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (confirmResult != DialogResult.Yes) return;

                bool deleted = await backupManager.DeleteBackupAsync(backup);

                if (deleted)
                {
                    MessageBox.Show(
                        "백업이 삭제되었습니다.",
                        "삭제 완료",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );

                    // Refresh list
                    string currentFilter = cmbCharacterFilter.SelectedIndex > 0
                        ? cmbCharacterFilter.SelectedItem.ToString()
                        : null;
                    await LoadBackupsAsync(currentFilter);
                }
                else
                {
                    MessageBox.Show(
                        "백업 삭제에 실패했습니다.",
                        "삭제 실패",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"삭제 중 오류 발생:\n{ex.Message}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private async void BtnRefresh_Click(object sender, EventArgs e)
        {
            string currentFilter = cmbCharacterFilter.SelectedIndex > 0
                ? cmbCharacterFilter.SelectedItem.ToString()
                : null;

            await LoadBackupsAsync(currentFilter);
        }
    }
}
