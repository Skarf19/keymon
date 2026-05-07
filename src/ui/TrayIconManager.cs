using H.NotifyIcon;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Drawing;

namespace Keymon
{
    public class TrayIconManager : IDisposable
    {
        private TaskbarIcon? _notifyIcon;
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Keymon";

        private DispatcherTimer? _animTimer;
        private readonly Dictionary<int, List<Icon>> _animationGroups = new();
        private int _currentAnimationNum = 1;
        private int _currentFrameIdx = 0;

        public Action? OnShowDashboard;
        public Action? OnResetData;
        public Action? OnExit;

        public void Initialize()
        {
            _notifyIcon = new TaskbarIcon();
            _notifyIcon.ToolTipText = "데이터 수집 중...";

            LoadAllAnimationFrames();

            if (_animationGroups.Count == 0)
            {
                MessageBox.Show("애니메이션 파일을 찾을 수 없습니다.");
                return;
            }
            _notifyIcon.Icon = _animationGroups[1][0];

            _notifyIcon.Visibility = Visibility.Visible;
            _notifyIcon.ForceCreate();
            _notifyIcon.ContextMenu = CreateContextMenu();
            _notifyIcon.TrayLeftMouseDown += (s, e) => OnShowDashboard?.Invoke();

            _animTimer = new DispatcherTimer(DispatcherPriority.Render);
            _animTimer.Interval = TimeSpan.FromMilliseconds(85);
            _animTimer.Tick += OnAnimationTick;
            _animTimer.Start();
        }

        private void LoadAllAnimationFrames()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            for (int animNum = 1; animNum <= 3; animNum++)
            {
                var frames = new List<Icon>();
                for (int i = 0; i < 12; i++)
                {
                    string path = Path.Combine(baseDir, "Assets", $"Anim{animNum}", $"{i}.png");

                    if (File.Exists(path))
                    {
                        try
                        {
                            using (var bitmap = new Bitmap(path))
                            {
                                using (var resized = new Bitmap(bitmap, new System.Drawing.Size(32, 32)))
                                {
                                    IntPtr hIcon = resized.GetHicon();
                                    frames.Add(Icon.FromHandle(hIcon));
                                }
                            }
                        }
                        catch { }
                    }
                }
                if (frames.Count > 0) _animationGroups[animNum] = frames;
            }
        }

        private void OnAnimationTick(object? sender, EventArgs e)
        {
            if (_notifyIcon == null || !_animationGroups.ContainsKey(_currentAnimationNum)) return;

            var currentSet = _animationGroups[_currentAnimationNum];
            _notifyIcon.Icon = currentSet[_currentFrameIdx];
            _currentFrameIdx = (_currentFrameIdx + 1) % currentSet.Count;
        }

        public void UpdateAnimationByState(int focusState)
        {
            int nextAnimNum = focusState switch { 0 => 1, 1 or 2 or 3 => 2, 4 => 3, _ => 1 };
            if (_currentAnimationNum != nextAnimNum)
            {
                _currentAnimationNum = nextAnimNum;
                _currentFrameIdx = 0;
            }
        }

        public void UpdateTooltip(string text) { if (_notifyIcon != null) _notifyIcon.ToolTipText = text; }

        private ContextMenu CreateContextMenu()
        {
            var menu = new ContextMenu();
            var dashItem = new MenuItem { Header = "대시보드 열기" };
            dashItem.Click += (s, e) => OnShowDashboard?.Invoke();
            menu.Items.Add(dashItem);
            menu.Items.Add(new Separator());

            var autoStartItem = new MenuItem { Header = "윈도우 시작 시 자동 실행", IsCheckable = true };
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                if (key?.GetValue(AppName) != null) autoStartItem.IsChecked = true;
            }
            autoStartItem.Click += (s, e) => ToggleAutoStart(autoStartItem);
            menu.Items.Add(autoStartItem);

            var resetItem = new MenuItem { Header = "데이터 초기화" };
            resetItem.Click += (s, e) => OnResetData?.Invoke();
            menu.Items.Add(resetItem);

            var exitItem = new MenuItem { Header = "종료" };
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
                    if (item.IsChecked) key.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
                    else key.DeleteValue(AppName, false);
                }
            }
        }

        public void Dispose()
        {
            _animTimer?.Stop();
            foreach (var group in _animationGroups.Values)
                foreach (var icon in group) icon.Dispose();
            _notifyIcon?.Dispose();
        }
    }
}