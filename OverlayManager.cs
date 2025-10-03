using System;
using System.Drawing;
using System.Windows.Forms;

namespace D2RSaveMonitor
{
    /// <summary>
    /// Manages warning overlay display
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

    /// <summary>
    /// 오버레이 경고 폼 / Overlay warning form
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
}
