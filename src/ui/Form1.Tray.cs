using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Test
{
    public partial class Form1
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;

        private void PrepareTrayIcon()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("열기", null, OnShowStatus);
            trayMenu.Items.Add("종료", null, OnExit);

            trayIcon = new NotifyIcon();
            trayIcon.Text = "백그라운드 키보드 분석";
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += OnShowStatus;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                trayIcon.ShowBalloonTip(2000, "알림", "백그라운드에서 분석 중입니다.", ToolTipIcon.Info);
            }
            base.OnFormClosing(e);
        }

        private void OnShowStatus(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
        }

        private void OnExit(object sender, EventArgs e)
        {
            UnhookWindowsHookEx(_hookID);
            trayIcon.Visible = false;
            unityProcess?.Kill();
            udpSender?.Close();
            Application.Exit();
        }
    }
}