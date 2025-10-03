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
        private GroupBox grpPeriodicScope;
        private RadioButton rdoScopeDanger;
        private RadioButton rdoScopeWarning;
        private RadioButton rdoScopeAll;
        private Label lblScopeHint;

        public BackupSettingsForm(BackupSettings currentSettings)
        {
            settings = currentSettings ?? new BackupSettings();
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            Text = LanguageManager.GetString("BackupSettingsTitle");
            Size = new Size(470, 540);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            // Auto backup on danger
            Label lblAutoBackup = new Label
            {
                Text = LanguageManager.GetString("AutoBackupSettings"),
                Location = new Point(20, 20),
                Size = new Size(200, 20),
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblAutoBackup);

            chkAutoBackupDanger = new CheckBox
            {
                Text = LanguageManager.GetString("AutoBackupDanger"),
                Location = new Point(40, 50),
                Size = new Size(350, 20)
            };
            Controls.Add(chkAutoBackupDanger);

            chkEnableCompression = new CheckBox
            {
                Text = LanguageManager.GetString("EnableCompression"),
                Location = new Point(40, 75),
                Size = new Size(350, 20)
            };
            Controls.Add(chkEnableCompression);

            // Periodic backup
            Label lblPeriodic = new Label
            {
                Text = LanguageManager.GetString("PeriodicBackupSettings"),
                Location = new Point(20, 115),
                Size = new Size(200, 20),
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblPeriodic);

            chkPeriodicBackup = new CheckBox
            {
                Text = LanguageManager.GetString("PeriodicBackupEnable"),
                Location = new Point(40, 145),
                Size = new Size(220, 20)
            };
            chkPeriodicBackup.CheckedChanged += ChkPeriodicBackup_CheckedChanged;
            Controls.Add(chkPeriodicBackup);

            grpPeriodicScope = new GroupBox
            {
                Text = LanguageManager.GetString("PeriodicScopeGroup"),
                Location = new Point(40, 175),
                Size = new Size(380, 130)
            };
            Controls.Add(grpPeriodicScope);

            rdoScopeDanger = new RadioButton
            {
                Text = LanguageManager.GetString("PeriodicScopeDangerLabel"),
                Location = new Point(15, 25),
                Size = new Size(280, 20)
            };
            grpPeriodicScope.Controls.Add(rdoScopeDanger);

            rdoScopeWarning = new RadioButton
            {
                Text = LanguageManager.GetString("PeriodicScopeWarningLabel"),
                Location = new Point(15, 50),
                Size = new Size(280, 20)
            };
            grpPeriodicScope.Controls.Add(rdoScopeWarning);

            rdoScopeAll = new RadioButton
            {
                Text = LanguageManager.GetString("PeriodicScopeAllLabel"),
                Location = new Point(15, 75),
                Size = new Size(280, 20)
            };
            grpPeriodicScope.Controls.Add(rdoScopeAll);

            lblScopeHint = new Label
            {
                Text = LanguageManager.GetString("PeriodicScopeHint"),
                Location = new Point(12, 100),
                Size = new Size(350, 20),
                ForeColor = Color.DimGray
            };
            grpPeriodicScope.Controls.Add(lblScopeHint);

            Label lblInterval = new Label
            {
                Text = LanguageManager.GetString("BackupIntervalMin"),
                Location = new Point(60, 320),
                Size = new Size(120, 20)
            };
            Controls.Add(lblInterval);

            nudPeriodicInterval = new NumericUpDown
            {
                Location = new Point(180, 318),
                Size = new Size(80, 20),
                Minimum = 5,
                Maximum = 240,
                Value = 30,
                Increment = 5
            };
            Controls.Add(nudPeriodicInterval);

            Label lblMinutes = new Label
            {
                Text = LanguageManager.GetString("Minutes"),
                Location = new Point(265, 320),
                Size = new Size(30, 20)
            };
            Controls.Add(lblMinutes);

            // Backup retention
            Label lblRetention = new Label
            {
                Text = LanguageManager.GetString("BackupRetentionSettings"),
                Location = new Point(20, 360),
                Size = new Size(200, 20),
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblRetention);

            Label lblMaxBackups = new Label
            {
                Text = LanguageManager.GetString("MaxBackupsPerFile"),
                Location = new Point(40, 390),
                Size = new Size(140, 20)
            };
            Controls.Add(lblMaxBackups);

            nudMaxBackups = new NumericUpDown
            {
                Location = new Point(180, 388),
                Size = new Size(80, 20),
                Minimum = 1,
                Maximum = 100,
                Value = 10
            };
            Controls.Add(nudMaxBackups);

            Label lblBackups = new Label
            {
                Text = LanguageManager.GetString("Count"),
                Location = new Point(265, 390),
                Size = new Size(30, 20)
            };
            Controls.Add(lblBackups);

            Label lblCooldown = new Label
            {
                Text = LanguageManager.GetString("BackupCooldownSec"),
                Location = new Point(40, 420),
                Size = new Size(140, 20)
            };
            Controls.Add(lblCooldown);

            nudCooldown = new NumericUpDown
            {
                Location = new Point(180, 418),
                Size = new Size(80, 20),
                Minimum = 10,
                Maximum = 300,
                Value = 60,
                Increment = 10
            };
            Controls.Add(nudCooldown);

            Label lblSeconds = new Label
            {
                Text = LanguageManager.GetString("Seconds"),
                Location = new Point(265, 420),
                Size = new Size(30, 20)
            };
            Controls.Add(lblSeconds);

            // Buttons
            btnResetDefaults = new Button
            {
                Text = LanguageManager.GetString("ResetDefaults"),
                Location = new Point(20, 460),
                Size = new Size(120, 30)
            };
            btnResetDefaults.Click += BtnResetDefaults_Click;
            Controls.Add(btnResetDefaults);

            btnCancel = new Button
            {
                Text = LanguageManager.GetString("Cancel"),
                Location = new Point(240, 460),
                Size = new Size(90, 30),
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(btnCancel);

            btnSave = new Button
            {
                Text = LanguageManager.GetString("Save"),
                Location = new Point(340, 460),
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
            SelectScopeRadioButton(settings.PeriodicScope);
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
            grpPeriodicScope.Enabled = chkPeriodicBackup.Checked;
        }

        private void BtnResetDefaults_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                LanguageManager.GetString("ResetConfirm"),
                LanguageManager.GetString("ResetConfirmTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result == DialogResult.Yes)
            {
                var defaults = new BackupSettings();
                chkAutoBackupDanger.Checked = defaults.AutoBackupOnDanger;
                chkEnableCompression.Checked = defaults.EnableCompression;
                chkPeriodicBackup.Checked = defaults.PeriodicBackupEnabled;
                SelectScopeRadioButton(defaults.PeriodicScope);
                nudPeriodicInterval.Value = defaults.PeriodicIntervalMinutes;
                nudMaxBackups.Value = defaults.MaxBackupsPerFile;
                nudCooldown.Value = defaults.BackupCooldownSeconds;
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                settings.AutoBackupOnDanger = chkAutoBackupDanger.Checked;
                settings.EnableCompression = chkEnableCompression.Checked;
                settings.PeriodicBackupEnabled = chkPeriodicBackup.Checked;
                settings.PeriodicScope = GetSelectedScope();
                settings.PeriodicIntervalMinutes = (int)nudPeriodicInterval.Value;
                settings.MaxBackupsPerFile = (int)nudMaxBackups.Value;
                settings.BackupCooldownSeconds = (int)nudCooldown.Value;

                SettingsManager.SaveSettings(settings);

                MessageBox.Show(
                    LanguageManager.GetString("SaveSuccess"),
                    LanguageManager.GetString("SaveSuccessTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LanguageManager.GetString("SettingsSaveError"), ex.Message),
                    LanguageManager.GetString("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                DialogResult = DialogResult.Cancel;
            }
        }

        private void SelectScopeRadioButton(PeriodicBackupScope scope)
        {
            switch (scope)
            {
                case PeriodicBackupScope.DangerOnly:
                    rdoScopeDanger.Checked = true;
                    break;
                case PeriodicBackupScope.EntireRange:
                    rdoScopeAll.Checked = true;
                    break;
                default:
                    rdoScopeWarning.Checked = true;
                    break;
            }
        }

        private PeriodicBackupScope GetSelectedScope()
        {
            if (rdoScopeDanger.Checked)
            {
                return PeriodicBackupScope.DangerOnly;
            }

            if (rdoScopeAll.Checked)
            {
                return PeriodicBackupScope.EntireRange;
            }

            return PeriodicBackupScope.WarningOrAbove;
        }
    }
}
