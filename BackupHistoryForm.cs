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
            Size = new Size(900, 650);
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
            dgvBackups.Columns.Add("Timestamp", "백업 시간");
            dgvBackups.Columns.Add("Character", "캐릭터");
            dgvBackups.Columns.Add("Size", "파일 크기");
            dgvBackups.Columns.Add("Compression", "압축");
            dgvBackups.Columns.Add("Trigger", "백업 원인");
            dgvBackups.Columns.Add("Type", "유형");

            dgvBackups.Columns["Timestamp"].Width = 150;
            dgvBackups.Columns["Character"].Width = 150;
            dgvBackups.Columns["Size"].Width = 100;
            dgvBackups.Columns["Compression"].Width = 100;
            dgvBackups.Columns["Trigger"].Width = 120;
            dgvBackups.Columns["Type"].Width = 100;

            // Backup info label
            lblBackupInfo = new Label
            {
                Text = "백업을 선택하면 상세 정보가 표시됩니다.",
                Location = new Point(20, 470),
                Size = new Size(850, 80),  // 40 → 80 (여러 줄 텍스트 표시)
                ForeColor = Color.Gray,
                AutoSize = false  // 고정 크기 사용
            };
            Controls.Add(lblBackupInfo);

            // Buttons
            btnRestore = new Button
            {
                Text = "복원",
                Location = new Point(550, 565),  // 520 → 565
                Size = new Size(100, 30),
                Enabled = false
            };
            btnRestore.Click += BtnRestore_Click;
            Controls.Add(btnRestore);

            btnDelete = new Button
            {
                Text = "삭제",
                Location = new Point(660, 565),  // 520 → 565
                Size = new Size(100, 30),
                Enabled = false
            };
            btnDelete.Click += BtnDelete_Click;
            Controls.Add(btnDelete);

            btnClose = new Button
            {
                Text = "닫기",
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
                lblBackupInfo.Text = "백업 목록 로딩 중...";
                lblBackupInfo.ForeColor = Color.Gray;

                // Force UI update before starting background work
                Application.DoEvents();

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
                        $"백업 목록 로드 실패:\n{ex.Message}",
                        "오류",
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
                    cmbCharacterFilter.Items.Add("모든 캐릭터");

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
                    : allBackups.Where(b => b.OriginalFile == characterFilter).ToList();

                // Add to grid - 배치 처리로 성능 향상
                if (filteredBackups.Count > 0)
                {
                    dgvBackups.Rows.Add(filteredBackups.Count);

                    for (int i = 0; i < filteredBackups.Count; i++)
                    {
                        var backup = filteredBackups[i];
                        string trigger = GetTriggerDisplayName(backup.TriggerReason);
                        string type = backup.IsAutomatic ? "자동" : "수동";

                        // 압축 정보 생성
                        string compressionInfo;
                        if (backup.IsCompressed)
                        {
                            double ratio = backup.GetCompressionRatio();
                            compressionInfo = $"압축 {ratio:F0}%";
                        }
                        else
                        {
                            compressionInfo = "-";
                        }

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
                    ? "백업이 없습니다."
                    : "백업을 선택하면 상세 정보가 표시됩니다.";
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
                    $"필터 적용 중 오류 발생:\n{ex.Message}",
                    "오류",
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
                        lblBackupInfo.Text = $"선택된 백업:\n" +
                                            $"캐릭터: {backup.OriginalFile}\n" +
                                            $"백업 파일: {backup.BackupFile}\n" +
                                            $"크기: {backup.FileSize} bytes ({(double)backup.FileSize / FileConstants.MaxFileSize * 100:F1}%)";
                    }

                    btnRestore.Text = "복원";
                    btnDelete.Text = "삭제";
                }
                // 다중 선택인 경우 선택 개수 표시
                else
                {
                    lblBackupInfo.Text = $"선택된 백업: {selectedCount}개";
                    btnRestore.Text = $"복원 ({selectedCount}개)";
                    btnDelete.Text = $"삭제 ({selectedCount}개)";
                }
            }
            else
            {
                btnRestore.Enabled = false;
                btnDelete.Enabled = false;
                lblBackupInfo.Text = "백업을 선택하면 상세 정보가 표시됩니다.";
                btnRestore.Text = "복원";
                btnDelete.Text = "삭제";
            }
        }

        private async void BtnRestore_Click(object sender, EventArgs e)
        {
            if (dgvBackups.SelectedRows.Count == 0) return;

            // 다중 선택된 경우 경고
            if (dgvBackups.SelectedRows.Count > 1)
            {
                MessageBox.Show(
                    "복원은 한 번에 하나의 백업만 가능합니다.\n첫 번째 선택된 백업만 복원됩니다.",
                    "알림",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }

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
                    confirmMessage = $"다음 백업을 삭제하시겠습니까?\n\n" +
                                   $"캐릭터: {backup.OriginalFile}\n" +
                                   $"백업 시간: {backup.Timestamp:yyyy-MM-dd HH:mm:ss}\n\n" +
                                   $"이 작업은 되돌릴 수 없습니다.";
                }
                else
                {
                    confirmMessage = $"{selectedBackups.Count}개의 백업을 삭제하시겠습니까?\n\n" +
                                   $"이 작업은 되돌릴 수 없습니다.";
                }

                var confirmResult = MessageBox.Show(
                    confirmMessage,
                    "백업 삭제 확인",
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
                if (selectedBackups.Count == 1)
                {
                    resultMessage = successCount > 0 ? "백업이 삭제되었습니다." : "백업 삭제에 실패했습니다.";
                }
                else
                {
                    resultMessage = $"삭제 완료: {successCount}개 성공";
                    if (failCount > 0)
                    {
                        resultMessage += $", {failCount}개 실패";
                    }
                }

                MessageBox.Show(
                    resultMessage,
                    failCount > 0 ? "삭제 완료 (일부 실패)" : "삭제 완료",
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
                    $"삭제 중 오류 발생:\n{ex.Message}",
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
                    $"새로고침 중 오류 발생:\n{ex.Message}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
    }
}
