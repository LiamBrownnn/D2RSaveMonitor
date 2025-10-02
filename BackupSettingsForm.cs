using System;
using System.Drawing;
using System.Windows.Forms;

namespace D2RSaveMonitor
{
    /// <summary>
    /// Backup settings configuration dialog
    /// </summary>
    public class BackupSettingsForm : Form
    {
        private readonly BackupSettings settings;

        private CheckBox chkAutoBackupDanger;
        private CheckBox chkPeriodicBackup;
        private CheckBox chkEnableCompression;
        private NumericUpDown nudPeriodicInterval;
        private NumericUpDown nudMaxBackups;
        private NumericUpDown nudCooldown;
        private Button btnSave;
        private Button btnCancel;
        private Button btnResetDefaults;

        public BackupSettingsForm(BackupSettings currentSettings)
        {
            settings = currentSettings ?? new BackupSettings();
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            Text = "백업 설정";
            Size = new Size(450, 390);  // 높이 증가 (350 → 390)
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            // Auto backup on danger
            Label lblAutoBackup = new Label
            {
                Text = "자동 백업 설정:",
                Location = new Point(20, 20),
                Size = new Size(200, 20),
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblAutoBackup);

            chkAutoBackupDanger = new CheckBox
            {
                Text = "위험 수준(7500 bytes) 도달 시 자동 백업",
                Location = new Point(40, 50),
                Size = new Size(350, 20)
            };
            Controls.Add(chkAutoBackupDanger);

            chkEnableCompression = new CheckBox
            {
                Text = "백업 파일 압축 (디스크 공간 50~70% 절약)",
                Location = new Point(40, 75),
                Size = new Size(350, 20)
            };
            Controls.Add(chkEnableCompression);

            // Periodic backup
            Label lblPeriodic = new Label
            {
                Text = "주기적 백업 설정:",
                Location = new Point(20, 115),  // 90 → 115
                Size = new Size(200, 20),
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblPeriodic);

            chkPeriodicBackup = new CheckBox
            {
                Text = "주기적 자동 백업 활성화",
                Location = new Point(40, 145),  // 120 → 145
                Size = new Size(200, 20)
            };
            chkPeriodicBackup.CheckedChanged += ChkPeriodicBackup_CheckedChanged;
            Controls.Add(chkPeriodicBackup);

            Label lblInterval = new Label
            {
                Text = "백업 주기(분):",
                Location = new Point(60, 175),  // 150 → 175
                Size = new Size(120, 20)
            };
            Controls.Add(lblInterval);

            nudPeriodicInterval = new NumericUpDown
            {
                Location = new Point(180, 173),  // 148 → 173
                Size = new Size(80, 20),
                Minimum = 5,
                Maximum = 240,
                Value = 30,
                Increment = 5
            };
            Controls.Add(nudPeriodicInterval);

            Label lblMinutes = new Label
            {
                Text = "분",
                Location = new Point(265, 175),  // 150 → 175
                Size = new Size(30, 20)
            };
            Controls.Add(lblMinutes);

            // Backup retention
            Label lblRetention = new Label
            {
                Text = "백업 보관 설정:",
                Location = new Point(20, 215),  // 190 → 215
                Size = new Size(200, 20),
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblRetention);

            Label lblMaxBackups = new Label
            {
                Text = "파일당 최대 백업 개수:",
                Location = new Point(40, 245),  // 220 → 245
                Size = new Size(140, 20)
            };
            Controls.Add(lblMaxBackups);

            nudMaxBackups = new NumericUpDown
            {
                Location = new Point(180, 243),  // 218 → 243
                Size = new Size(80, 20),
                Minimum = 1,
                Maximum = 100,
                Value = 10
            };
            Controls.Add(nudMaxBackups);

            Label lblBackups = new Label
            {
                Text = "개",
                Location = new Point(265, 245),  // 220 → 245
                Size = new Size(30, 20)
            };
            Controls.Add(lblBackups);

            Label lblCooldown = new Label
            {
                Text = "자동 백업 쿨다운(초):",
                Location = new Point(40, 275),  // 250 → 275
                Size = new Size(140, 20)
            };
            Controls.Add(lblCooldown);

            nudCooldown = new NumericUpDown
            {
                Location = new Point(180, 273),  // 248 → 273
                Size = new Size(80, 20),
                Minimum = 10,
                Maximum = 300,
                Value = 60,
                Increment = 10
            };
            Controls.Add(nudCooldown);

            Label lblSeconds = new Label
            {
                Text = "초",
                Location = new Point(265, 275),  // 250 → 275
                Size = new Size(30, 20)
            };
            Controls.Add(lblSeconds);

            // Buttons
            btnResetDefaults = new Button
            {
                Text = "기본값으로 복원",
                Location = new Point(20, 315),  // 280 → 315
                Size = new Size(120, 30)
            };
            btnResetDefaults.Click += BtnResetDefaults_Click;
            Controls.Add(btnResetDefaults);

            btnCancel = new Button
            {
                Text = "취소",
                Location = new Point(240, 315),  // 280 → 315
                Size = new Size(90, 30),
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(btnCancel);

            btnSave = new Button
            {
                Text = "저장",
                Location = new Point(340, 315),  // 280 → 315
                Size = new Size(90, 30),
                DialogResult = DialogResult.OK
            };
            btnSave.Click += BtnSave_Click;
            Controls.Add(btnSave);

            AcceptButton = btnSave;
            CancelButton = btnCancel;
        }

        private void LoadSettings()
        {
            chkAutoBackupDanger.Checked = settings.AutoBackupOnDanger;
            chkEnableCompression.Checked = settings.EnableCompression;
            chkPeriodicBackup.Checked = settings.PeriodicBackupEnabled;
            nudPeriodicInterval.Value = Math.Max(nudPeriodicInterval.Minimum, Math.Min(nudPeriodicInterval.Maximum, settings.PeriodicIntervalMinutes));
            nudMaxBackups.Value = Math.Max(nudMaxBackups.Minimum, Math.Min(nudMaxBackups.Maximum, settings.MaxBackupsPerFile));
            nudCooldown.Value = Math.Max(nudCooldown.Minimum, Math.Min(nudCooldown.Maximum, settings.BackupCooldownSeconds));

            UpdatePeriodicControls();
        }

        private void ChkPeriodicBackup_CheckedChanged(object sender, EventArgs e)
        {
            UpdatePeriodicControls();
        }

        private void UpdatePeriodicControls()
        {
            nudPeriodicInterval.Enabled = chkPeriodicBackup.Checked;
        }

        private void BtnResetDefaults_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "모든 설정을 기본값으로 복원하시겠습니까?",
                "기본값 복원",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result == DialogResult.Yes)
            {
                var defaults = new BackupSettings();
                chkAutoBackupDanger.Checked = defaults.AutoBackupOnDanger;
                chkEnableCompression.Checked = defaults.EnableCompression;
                chkPeriodicBackup.Checked = defaults.PeriodicBackupEnabled;
                nudPeriodicInterval.Value = defaults.PeriodicIntervalMinutes;
                nudMaxBackups.Value = defaults.MaxBackupsPerFile;
                nudCooldown.Value = defaults.BackupCooldownSeconds;
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                // Update settings
                settings.AutoBackupOnDanger = chkAutoBackupDanger.Checked;
                settings.EnableCompression = chkEnableCompression.Checked;
                settings.PeriodicBackupEnabled = chkPeriodicBackup.Checked;
                settings.PeriodicIntervalMinutes = (int)nudPeriodicInterval.Value;
                settings.MaxBackupsPerFile = (int)nudMaxBackups.Value;
                settings.BackupCooldownSeconds = (int)nudCooldown.Value;

                // Save to registry
                settings.SaveToRegistry();

                MessageBox.Show(
                    "설정이 저장되었습니다.\n\n참고: 압축 설정은 새로 생성되는 백업부터 적용됩니다.",
                    "저장 완료",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"설정 저장 중 오류 발생:\n{ex.Message}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                DialogResult = DialogResult.None; // Prevent dialog from closing
            }
        }
    }
}
