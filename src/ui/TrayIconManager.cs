using H.NotifyIcon;
using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Project1
{
    public class TrayIconManager : IDisposable
    {
        private TaskbarIcon? _notifyIcon;
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Project1";

        // App에서 처리할 동작들을 연결할 액션(Action)들
        public Action? OnShowDashboard;
        public Action? OnResetData;
        public Action? OnExit;

        public void Initialize()
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (!File.Exists(iconPath)) return;

            _notifyIcon = new TaskbarIcon();
            _notifyIcon.ToolTipText = "데이터 수집 시작 중...";

            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            _notifyIcon.IconSource = bitmap;
            _notifyIcon.Visibility = Visibility.Visible;
            _notifyIcon.ForceCreate();

            // 메뉴 생성
            _notifyIcon.ContextMenu = CreateContextMenu();
            _notifyIcon.TrayLeftMouseDoubleClick += (s, e) => OnShowDashboard?.Invoke();
        }

        private ContextMenu CreateContextMenu()
        {
            var menu = new ContextMenu();

            var detailsItem = new MenuItem { Header = "📊 대시보드 자세히 보기", FontWeight = FontWeights.Bold };
            detailsItem.Click += (s, e) => OnShowDashboard?.Invoke();
            menu.Items.Add(detailsItem);

            menu.Items.Add(new Separator());

            // 자동 실행 설정 메뉴
            var autoStartItem = new MenuItem { Header = "윈도우 시작 시 자동 실행", IsCheckable = true };
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                if (key?.GetValue(AppName) != null) autoStartItem.IsChecked = true;
            }

            autoStartItem.Click += (s, e) => ToggleAutoStart(autoStartItem);
            menu.Items.Add(autoStartItem);

            var resetItem = new MenuItem { Header = "데이터 전체 초기화" };
            resetItem.Click += (s, e) => OnResetData?.Invoke();
            menu.Items.Add(resetItem);

            var exitItem = new MenuItem { Header = "프로그램 종료" };
            exitItem.Click += (s, e) => OnExit?.Invoke();
            menu.Items.Add(exitItem);

            return menu;
        }

        private void ToggleAutoStart(MenuItem item)
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key != null)
                {
                    if (item.IsChecked)
                    {
                        string exePath = Environment.ProcessPath ?? "";
                        key.SetValue(AppName, $"\"{exePath}\"");
                    }
                    else key.DeleteValue(AppName, false);
                }
            }
        }

        public void UpdateTooltip(string text)
        {
            if (_notifyIcon != null) _notifyIcon.ToolTipText = text;
        }

        public void Dispose()
        {
            _notifyIcon?.Dispose();
        }
    }
}