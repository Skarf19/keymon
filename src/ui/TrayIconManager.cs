using H.NotifyIcon;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Keymon
{
    public class TrayIconManager : IDisposable
    {
        private TaskbarIcon? _notifyIcon;
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Keymon";

        private DispatcherTimer? _animTimer;

        private readonly Dictionary<int, List<System.Drawing.Icon>> _animationGroups = new();
        private int _currentAnimationNum = 1;
        private int _targetAnimationNum = 1;
        private int _currentFrameIdx = 0;
        private int _transitionDelayCounter = 0;

        public Action? OnShowDashboard;
        // 💡 삭제됨: public Action? OnResetData; 
        public Action? OnExit;
        public Action<bool>? OnToggleOverlay;

        public Action? OnToggleManualStandby;
        private MenuItem? _manualStandbyItem;

        public bool IsStandby { get; set; } = false;

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
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string baseDir = Path.GetFullPath(Path.Combine(exeDir, @"..\..\..\"));
            for (int animNum = 1; animNum <= 5; animNum++)
            {
                var frames = new List<System.Drawing.Icon>();
                for (int i = 1; i <= 8; i++)
                {
                    string path = Path.Combine(baseDir, "Assets", $"TrayAsset{animNum}", $"{i}.png");

                    if (File.Exists(path))
                    {
                        try
                        {
                            using (var bitmap = new System.Drawing.Bitmap(path))
                            {
                                using (var resized = new System.Drawing.Bitmap(bitmap, new System.Drawing.Size(32, 32)))
                                {
                                    IntPtr hIcon = resized.GetHicon();
                                    frames.Add(System.Drawing.Icon.FromHandle(hIcon));
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
            if (_notifyIcon == null) return;

            if (IsStandby)
            {
                if (_animationGroups.ContainsKey(1) && _animationGroups[1].Count > 0)
                {
                    _notifyIcon.Icon = _animationGroups[1][0];
                }
                _currentAnimationNum = 1;
                _targetAnimationNum = 1;
                _currentFrameIdx = 0;
                _transitionDelayCounter = 0;
                return;
            }

            if (!_animationGroups.ContainsKey(_currentAnimationNum)) return;

            var currentSet = _animationGroups[_currentAnimationNum];

            if (_currentFrameIdx < currentSet.Count)
            {
                _notifyIcon.Icon = currentSet[_currentFrameIdx];
            }

            if (_currentFrameIdx == 0)
            {
                _transitionDelayCounter++;
                if (_transitionDelayCounter < 24)
                {
                    return;
                }
                _transitionDelayCounter = 0;
            }

            _currentFrameIdx++;

            if (_currentFrameIdx >= currentSet.Count)
            {
                if (_currentAnimationNum != _targetAnimationNum)
                {
                    _currentAnimationNum = _targetAnimationNum;
                    _currentFrameIdx = 0;
                    _transitionDelayCounter = 24;
                }
                else
                {
                    _currentFrameIdx = 1;
                }
            }
        }

        public void UpdateAnimationByState(int focusState)
        {
            int nextAnimNum = focusState switch
            {
                0 => 1,
                1 => 2,
                2 => 3,
                3 => 4,
                4 => 5,
                _ => 1
            };

            _targetAnimationNum = nextAnimNum;
        }

        public void UpdateTooltip(string text) { if (_notifyIcon != null) _notifyIcon.ToolTipText = text; }

        private ContextMenu CreateContextMenu()
        {
            var menu = new ContextMenu();
            var dashItem = new MenuItem { Header = "대시보드 열기" };
            dashItem.Click += (s, e) => OnShowDashboard?.Invoke();
            menu.Items.Add(dashItem);

            menu.Items.Add(new Separator());

            _manualStandbyItem = new MenuItem { Header = "분석 일시 정지 (수동)", IsCheckable = true };
            _manualStandbyItem.Click += (s, e) => OnToggleManualStandby?.Invoke();
            menu.Items.Add(_manualStandbyItem);

            menu.Items.Add(new Separator());

            var overlayToggleItem = new MenuItem { Header = "오버레이 창 표시", IsCheckable = true, IsChecked = true };
            overlayToggleItem.Click += (s, e) => OnToggleOverlay?.Invoke(overlayToggleItem.IsChecked);
            menu.Items.Add(overlayToggleItem);

            menu.Items.Add(new Separator());

            var autoStartItem = new MenuItem { Header = "윈도우 시작 시 자동 실행", IsCheckable = true };
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                if (key?.GetValue(AppName) != null) autoStartItem.IsChecked = true;
            }
            autoStartItem.Click += (s, e) => ToggleAutoStart(autoStartItem);
            menu.Items.Add(autoStartItem);

            // 💡 삭제됨: 데이터 초기화 (resetItem) 메뉴 제거

            var exitItem = new MenuItem { Header = "종료" };
            exitItem.Click += (s, e) => OnExit?.Invoke();
            menu.Items.Add(exitItem);

            return menu;
        }

        public void SyncManualStandbyState(bool isManualStandby)
        {
            if (_manualStandbyItem != null)
                _manualStandbyItem.IsChecked = isManualStandby;
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