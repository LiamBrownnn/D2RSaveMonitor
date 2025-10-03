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
        private bool isUpdatingComboBox = false;  // ComboBox 프로그래매틱 업데이트 플래그

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

            // Subscribe to language change event
            LanguageManager.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnLanguageChanged(sender, e)));
                return;
            }

            UpdateUILanguage();
        }

        private void UpdateUILanguage()
        {
            // Update form title
            Text = LanguageManager.GetString("BackupHistoryTitle");

            // Update buttons
            btnRefresh.Text = LanguageManager.GetString("Refresh");
            btnRestore.Text = LanguageManager.GetString("Restore");
            btnDelete.Text = LanguageManager.GetString("Delete");
            btnClose.Text = LanguageManager.GetString("Close");

            // Update column headers
            dgvBackups.Columns["Timestamp"].HeaderText = LanguageManager.GetString("ColumnBackupTime");
            dgvBackups.Columns["Character"].HeaderText = LanguageManager.GetString("ColumnCharacter");
            dgvBackups.Columns["Size"].HeaderText = LanguageManager.GetString("ColumnFileSize");
            dgvBackups.Columns["Compression"].HeaderText = LanguageManager.GetString("ColumnCompression");
            dgvBackups.Columns["Trigger"].HeaderText = LanguageManager.GetString("ColumnTrigger");
            dgvBackups.Columns["Type"].HeaderText = LanguageManager.GetString("ColumnType");

            // Update backup info placeholder
            if (dgvBackups.SelectedRows.Count == 0)
            {
                lblBackupInfo.Text = LanguageManager.GetString("BackupInfoPlaceholder");
            }

            // Refresh grid to update trigger and type text
            if (dgvBackups.Rows.Count > 0)
            {
                _ = LoadBackupsAsync(cmbCharacterFilter.SelectedItem?.ToString());
            }
        }

        private async void OnFormShown(object sender, EventArgs e)
        {
            // Unhook to prevent multiple calls
            this.Shown -= OnFormShown;

            // UI가 완전히 준비될 때까지 대기
            await Task.Delay(50);

            try
            {
                await LoadBackupsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{LanguageManager.GetString("BackupLoadFailed")}:\n{ex.Message}",
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private void InitializeComponent()
        {
            Text = LanguageManager.GetString("BackupHistoryTitle");
            Size = new Size(900, 650);
            StartPosition = FormStartPosition.CenterParent;

            // Character filter
            Label lblFilter = new Label
            {
                Text = LanguageManager.GetString("CharacterFilter"),
                Location = new Point(20, 20),
                Size = new Size(100, 20)
            };
            Controls.Add(lblFilter);

            cmbCharacterFilter = new ComboBox
            {
                Location = new Point(125, 18),
                Size = new Size(200, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbCharacterFilter.SelectedIndexChanged += CmbCharacterFilter_SelectedIndexChanged;
            Controls.Add(cmbCharacterFilter);

            // Refresh button
            btnRefresh = new Button
            {
                Text = LanguageManager.GetString("Refresh"),
                Location = new Point(340, 17),
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
                MultiSelect = true,  // Ctrl 키로 여러 백업 선택 가능
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToResizeColumns = false,  // 컬럼 너비 조절 금지 (가로)
                AllowUserToResizeRows = false,  // 행 높이 조절 금지 (세로)
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing  // 헤더 높이 고정
            };
            dgvBackups.SelectionChanged += DgvBackups_SelectionChanged;
            Controls.Add(dgvBackups);

            // Setup columns
            dgvBackups.Columns.Add("Timestamp", LanguageManager.GetString("ColumnBackupTime"));
            dgvBackups.Columns.Add("Character", LanguageManager.GetString("ColumnCharacter"));
            dgvBackups.Columns.Add("Size", LanguageManager.GetString("ColumnFileSize"));
            dgvBackups.Columns.Add("Compression", LanguageManager.GetString("ColumnCompression"));
            dgvBackups.Columns.Add("Trigger", LanguageManager.GetString("ColumnTrigger"));
            dgvBackups.Columns.Add("Type", LanguageManager.GetString("ColumnType"));

            dgvBackups.Columns["Timestamp"].Width = 150;
            dgvBackups.Columns["Character"].Width = 150;
            dgvBackups.Columns["Size"].Width = 100;
            dgvBackups.Columns["Compression"].Width = 100;
            dgvBackups.Columns["Trigger"].Width = 120;
            dgvBackups.Columns["Type"].Width = 100;

            // Backup info label
            lblBackupInfo = new Label
            {
                Text = LanguageManager.GetString("BackupInfoPlaceholder"),
                Location = new Point(20, 470),
                Size = new Size(850, 80),  // 40 → 80 (여러 줄 텍스트 표시)
                ForeColor = Color.Gray,
                AutoSize = false  // 고정 크기 사용
            };
            Controls.Add(lblBackupInfo);

            // Buttons
            btnRestore = new Button
            {
                Text = LanguageManager.GetString("Restore"),
                Location = new Point(550, 565),  // 520 → 565
                Size = new Size(100, 30),
                Enabled = false
            };
            btnRestore.Click += BtnRestore_Click;
            Controls.Add(btnRestore);

            btnDelete = new Button
            {
                Text = LanguageManager.GetString("Delete"),
                Location = new Point(660, 565),  // 520 → 565
                Size = new Size(100, 30),
                Enabled = false
            };
            btnDelete.Click += BtnDelete_Click;
            Controls.Add(btnDelete);

            btnClose = new Button
            {
                Text = LanguageManager.GetString("Close"),
                Location = new Point(770, 565),  // 520 → 565
                Size = new Size(100, 30),
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(btnClose);

            CancelButton = btnClose;
        }

        private async Task LoadBackupsAsync(string characterFilter = null)
        {
            // Prevent re-entrance
            if (isLoading)
            {
                return;
            }

            try
            {
                isLoading = true;

                // Show loading state - UI 컨트롤 비활성화
                dgvBackups.Enabled = false;
                cmbCharacterFilter.Enabled = false;
                btnRefresh.Enabled = false;
                btnRestore.Enabled = false;
                btnDelete.Enabled = false;
                lblBackupInfo.Text = LanguageManager.GetString("LoadingBackups");
                lblBackupInfo.ForeColor = Color.Gray;

                // UI 업데이트를 한 프레임 비워 사용자에게 로딩 상태를 보여준다
                await Task.Yield();

                // Load backups in background thread
                var allBackups = await Task.Run(() => backupManager.GetAllBackups());

                // Check if still valid (form not disposed)
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                // Update UI on main thread
                UpdateUI(allBackups, characterFilter);
            }
            catch (ObjectDisposedException)
            {
                // Form was closed during loading - ignore silently
            }
            catch (Exception ex)
            {
                // Check if form is still valid before showing dialog
                if (!IsDisposed && IsHandleCreated)
                {
                    MessageBox.Show(
                        string.Format(LanguageManager.GetString("BackupLoadFailed"), ex.Message),
                        LanguageManager.GetString("Error"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
            finally
            {
                // UI 컨트롤 재활성화 전에 isLoading 플래그 먼저 해제
                isLoading = false;

                // Form이 유효한 경우에만 UI 업데이트
                if (!IsDisposed && IsHandleCreated)
                {
                    dgvBackups.Enabled = true;
                    cmbCharacterFilter.Enabled = true;
                    btnRefresh.Enabled = true;
                    // 버튼은 선택 상태에 따라 활성화됨 (SelectionChanged 이벤트에서 처리)
                }
            }
        }

        private void UpdateUI(List<BackupMetadata> allBackups, string characterFilter)
        {
            // UI 업데이트 시작 - ComboBox 이벤트 차단
            isUpdatingComboBox = true;

            dgvBackups.SuspendLayout();
            cmbCharacterFilter.BeginUpdate();
            try
            {
                dgvBackups.Rows.Clear();

                // Group by character for filter
                var characters = allBackups
                    .Select(b => b.OriginalFile)
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();

                // Update character filter dropdown - 이벤트 핸들러 완전 차단
                cmbCharacterFilter.SelectedIndexChanged -= CmbCharacterFilter_SelectedIndexChanged;
                try
                {
                    cmbCharacterFilter.Items.Clear();
                    cmbCharacterFilter.Items.Add(LanguageManager.GetString("AllCharacters"));

                    // 배치로 추가 (성능 향상)
                    if (characters.Count > 0)
                    {
                        cmbCharacterFilter.Items.AddRange(characters.ToArray());
                    }

                    // 필터에 맞는 인덱스 선택
                    if (!string.IsNullOrEmpty(characterFilter))
                    {
                        int index = cmbCharacterFilter.Items.IndexOf(characterFilter);
                        cmbCharacterFilter.SelectedIndex = index >= 0 ? index : 0;
                    }
                    else
                    {
                        cmbCharacterFilter.SelectedIndex = 0;
                    }
                }
                finally
                {
                    // 이벤트 핸들러 재등록
                    cmbCharacterFilter.SelectedIndexChanged += CmbCharacterFilter_SelectedIndexChanged;
                }

                // Apply filter
                var filteredBackups = string.IsNullOrEmpty(characterFilter)
                    ? allBackups
                    : allBackups.Where(b => string.Equals(b.OriginalFile, characterFilter, StringComparison.OrdinalIgnoreCase)).ToList();

                // Add to grid - 배치 처리로 성능 향상
                if (filteredBackups.Count > 0)
                {
                    dgvBackups.Rows.Add(filteredBackups.Count);

                    for (int i = 0; i < filteredBackups.Count; i++)
                    {
                        var backup = filteredBackups[i];
                        string trigger = GetTriggerDisplayName(backup.TriggerReason);
                        string type = backup.IsAutomatic
                            ? LanguageManager.GetString("Automatic")
                            : LanguageManager.GetString("Manual");

                        // 압축 정보 생성
                        string compressionInfo = backup.IsCompressed
                            ? string.Format(LanguageManager.GetString("CompressionRatioFormat"), backup.GetCompressionRatio())
                            : LanguageManager.GetString("CompressionNotAvailable");

                        var row = dgvBackups.Rows[i];
                        row.SetValues(
                            backup.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                            Path.GetFileNameWithoutExtension(backup.OriginalFile),
                            $"{backup.FileSize} bytes",
                            compressionInfo,
                            trigger,
                            type
                        );

                        // Store metadata in row tag
                        row.Tag = backup;
                    }
                }

                lblBackupInfo.Text = dgvBackups.Rows.Count == 0
                    ? LanguageManager.GetString("NoBackups")
                    : LanguageManager.GetString("BackupInfoPlaceholder");
                lblBackupInfo.ForeColor = Color.Gray;
            }
            finally
            {
                cmbCharacterFilter.EndUpdate();
                dgvBackups.ResumeLayout();

                // UI 업데이트 완료 - ComboBox 이벤트 허용
                isUpdatingComboBox = false;
            }
        }

        private string GetTriggerDisplayName(BackupTrigger trigger)
        {
            switch (trigger)
            {
                case BackupTrigger.DangerThreshold:
                    return LanguageManager.GetString("TriggerDanger");
                case BackupTrigger.PeriodicAutomatic:
                    return LanguageManager.GetString("TriggerPeriodic");
                case BackupTrigger.ManualSingle:
                    return LanguageManager.GetString("TriggerManualSingle");
                case BackupTrigger.ManualBulk:
                    return LanguageManager.GetString("TriggerManualBulk");
                case BackupTrigger.PreRestore:
                    return LanguageManager.GetString("TriggerPreRestore");
                default:
                    return LanguageManager.GetString("Unknown");
            }
        }

        private async void CmbCharacterFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            // 프로그래매틱 업데이트 중이면 무시 (재귀 방지)
            if (isUpdatingComboBox || isLoading)
            {
                return;
            }

            // 유효한 선택인지 확인
            if (cmbCharacterFilter.SelectedIndex < 0)
            {
                return;
            }

            try
            {
                if (cmbCharacterFilter.SelectedIndex == 0)
                {
                    // "모든 캐릭터" 선택
                    await LoadBackupsAsync();
                }
                else
                {
                    // 특정 캐릭터 선택
                    string character = cmbCharacterFilter.SelectedItem?.ToString();
                    if (!string.IsNullOrEmpty(character))
                    {
                        await LoadBackupsAsync(character);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LanguageManager.GetString("FilterApplyError"), ex.Message),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private void DgvBackups_SelectionChanged(object sender, EventArgs e)
        {
            int selectedCount = dgvBackups.SelectedRows.Count;

            if (selectedCount > 0)
            {
                btnRestore.Enabled = true;
                btnDelete.Enabled = true;

                // 단일 선택인 경우 상세 정보 표시
                if (selectedCount == 1)
                {
                    var backup = dgvBackups.SelectedRows[0].Tag as BackupMetadata;
                    if (backup != null)
                    {
                        lblBackupInfo.Text = string.Format(
                            LanguageManager.GetString("SelectedBackupInfo"),
                            Path.GetFileNameWithoutExtension(backup.OriginalFile),
                            backup.BackupFile,
                            backup.FileSize,
                            (double)backup.FileSize / FileConstants.MaxFileSize * 100
                        );
                    }

                    btnRestore.Text = LanguageManager.GetString("Restore");
                    btnDelete.Text = LanguageManager.GetString("Delete");
                }
                // 다중 선택인 경우 선택 개수 표시
                else
                {
                    lblBackupInfo.Text = string.Format(
                        LanguageManager.GetString("SelectedBackupCount"),
                        selectedCount
                    );
                    btnRestore.Text = string.Format(
                        LanguageManager.GetString("RestoreWithCount"),
                        selectedCount
                    );
                    btnDelete.Text = string.Format(
                        LanguageManager.GetString("DeleteWithCount"),
                        selectedCount
                    );
                }
            }
            else
            {
                btnRestore.Enabled = false;
                btnDelete.Enabled = false;
                lblBackupInfo.Text = LanguageManager.GetString("BackupInfoPlaceholder");
                btnRestore.Text = LanguageManager.GetString("Restore");
                btnDelete.Text = LanguageManager.GetString("Delete");
            }
        }

        private async void BtnRestore_Click(object sender, EventArgs e)
        {
            if (dgvBackups.SelectedRows.Count == 0) return;

            // 다중 선택된 경우 경고
            if (dgvBackups.SelectedRows.Count > 1)
            {
                MessageBox.Show(
                    LanguageManager.GetString("RestoreOnlyOne"),
                    LanguageManager.GetString("Notice"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }

            var backup = dgvBackups.SelectedRows[0].Tag as BackupMetadata;
            if (backup == null) return;

            try
            {
                var confirmResult = MessageBox.Show(
                    string.Format(
                        LanguageManager.GetString("RestoreConfirm"),
                        Path.GetFileNameWithoutExtension(backup.OriginalFile),
                        backup.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                        backup.FileSize
                    ),
                    LanguageManager.GetString("RestoreConfirmTitle"),
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
                        string.Format(
                            LanguageManager.GetString("RestoreSuccess"),
                            Path.GetFileName(backup.OriginalFile),
                            result.PreRestoreBackup?.BackupFile ?? LanguageManager.GetString("None")
                        ),
                        LanguageManager.GetString("RestoreSuccessTitle"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );

                    DialogResult = DialogResult.OK; // Signal to refresh main form
                    Close();
                }
                else
                {
                    MessageBox.Show(
                        string.Format(LanguageManager.GetString("RestoreFailed"), result.ErrorMessage),
                        LanguageManager.GetString("RestoreFailedTitle"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LanguageManager.GetString("RestoreFailed"), ex.Message),
                    LanguageManager.GetString("Error"),
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

            try
            {
                // 선택된 모든 백업 수집
                var selectedBackups = new List<BackupMetadata>();
                foreach (DataGridViewRow row in dgvBackups.SelectedRows)
                {
                    var backup = row.Tag as BackupMetadata;
                    if (backup != null)
                    {
                        selectedBackups.Add(backup);
                    }
                }

                if (selectedBackups.Count == 0) return;

                // 확인 메시지 (단일/다중에 따라 다르게)
                string confirmMessage;
                if (selectedBackups.Count == 1)
                {
                    var backup = selectedBackups[0];
                    confirmMessage = string.Format(
                        LanguageManager.GetString("DeleteConfirm"),
                        Path.GetFileName(backup.OriginalFile),
                        backup.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")
                    );
                }
                else
                {
                    confirmMessage = string.Format(
                        LanguageManager.GetString("DeleteMultipleConfirm"),
                        selectedBackups.Count
                    );
                }

                var confirmResult = MessageBox.Show(
                    confirmMessage,
                    LanguageManager.GetString("DeleteConfirmTitle"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (confirmResult != DialogResult.Yes) return;

                btnRestore.Enabled = false;
                btnDelete.Enabled = false;
                btnRefresh.Enabled = false;

                // 모든 선택된 백업 삭제
                int successCount = 0;
                int failCount = 0;

                foreach (var backup in selectedBackups)
                {
                    bool deleted = await backupManager.DeleteBackupAsync(backup);
                    if (deleted)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }

                // 결과 메시지
                string resultMessage;
                string resultTitle;
                if (selectedBackups.Count == 1)
                {
                    if (successCount > 0)
                    {
                        resultMessage = LanguageManager.GetString("DeleteSuccess");
                        resultTitle = LanguageManager.GetString("DeleteSuccessTitle");
                    }
                    else
                    {
                        resultMessage = LanguageManager.GetString("DeleteFailed");
                        resultTitle = LanguageManager.GetString("DeleteFailedTitle");
                    }
                }
                else
                {
                    if (failCount > 0)
                    {
                        resultMessage = string.Format(LanguageManager.GetString("DeletePartialSuccess"), successCount, failCount);
                        resultTitle = LanguageManager.GetString("DeletePartialTitle");
                    }
                    else
                    {
                        resultMessage = string.Format(LanguageManager.GetString("DeleteMultipleSuccess"), successCount);
                        resultTitle = LanguageManager.GetString("DeleteSuccessTitle");
                    }
                }

                MessageBox.Show(
                    resultMessage,
                    resultTitle,
                    MessageBoxButtons.OK,
                    failCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information
                );

                // 목록 새로고침
                string currentFilter = cmbCharacterFilter.SelectedIndex > 0
                    ? cmbCharacterFilter.SelectedItem.ToString()
                    : null;
                await LoadBackupsAsync(currentFilter);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LanguageManager.GetString("DeleteFailedWithReason"), ex.Message),
                    LanguageManager.GetString("Error"),
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

        private async void BtnRefresh_Click(object sender, EventArgs e)
        {
            // 이미 로딩 중이면 무시
            if (isLoading)
            {
                return;
            }

            try
            {
                // 현재 필터 상태 유지
                string currentFilter = null;
                if (cmbCharacterFilter.SelectedIndex > 0 && cmbCharacterFilter.SelectedItem != null)
                {
                    currentFilter = cmbCharacterFilter.SelectedItem.ToString();
                }

                await LoadBackupsAsync(currentFilter);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LanguageManager.GetString("RefreshFailed"), ex.Message),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            LanguageManager.LanguageChanged -= OnLanguageChanged;
            base.OnFormClosed(e);
        }
    }
}
