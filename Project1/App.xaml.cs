using H.NotifyIcon;
using Microsoft.Win32;
using SharpHook;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Linq;

namespace Project1
{
    public partial class App : Application
    {
        // 1. 핵심 인스턴스
        private AnalysisEngine _engine = new AnalysisEngine();
        private DashboardWindow? _dashboardWindow;
        private TaskbarIcon? _notifyIcon;
        private TaskPoolGlobalHook? _globalHook;
        private DispatcherTimer? _timer;

        // 2. 수집 데이터 (실시간 큐)
        private Queue<DateTime> _keyTimes = new Queue<DateTime>();
        private Queue<DateTime> _mouseTimes = new Queue<DateTime>();
        private Queue<DateTime> _backspaceTimes = new Queue<DateTime>();
        private Queue<DateTime> _contextSwitchTimes = new Queue<DateTime>();
        private Queue<DateTime> _jerkTimes = new Queue<DateTime>();
        private Queue<DateTime> _mouseTurnTimes = new Queue<DateTime>();

        // 3. 상태 관리 변수
        private int _tickCounter = 0;
        private int _keyCount = 0;
        private int _mouseCount = 0;
        private int _backspaceCount = 0;
        private List<int> _historyScores = new List<int>();
        private List<int> _historyStates = new List<int>();

        private double _minuteTotalDt = 0;
        private int _minuteCountDt = 0;
        private double _minuteTotalFt = 0;
        private int _minuteCountFt = 0;

        private IntPtr _lastWindowHandle = IntPtr.Zero;
        private DateTime _lastKeyReleaseTime = DateTime.MinValue;
        private Dictionary<SharpHook.Data.KeyCode, DateTime> _pressedKeys = new Dictionary<SharpHook.Data.KeyCode, DateTime>();

        // 마우스 궤적 계산용
        private int _lastMouseX = -1;
        private int _lastMouseY = -1;
        private double _lastMouseAngle = -1000;
        private DateTime _lastMouseTime = DateTime.MinValue;

        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Project1";

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            LoadUserData();     // 1. 저장된 데이터 로드 (엔진으로 주입)
            SetupNotifyIcon();  // 2. 트레이 아이콘 설정
            SetupGlobalHook();  // 3. 후킹 시작

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.Now;

            // 과거 데이터 청소
            CleanOldData(now);

            _tickCounter++;
            MonitorWindowSwitch(now);

            // [실시간 분석] 엔진에게 현재 상태 물어보기
            _engine.UpdateRealtimeStatus(_keyTimes.Count, _mouseTimes.Count, _contextSwitchTimes.Count, _engine.IsFirstAnalysisComplete);

            // [60초 분석] 엔진에게 심층 계산 요청
            if (_tickCounter >= 60)
            {
                double avgDt = _minuteCountDt > 0 ? _minuteTotalDt / _minuteCountDt : _engine.PersonalEmaDt;
                double avgFt = _minuteCountFt > 0 ? _minuteTotalFt / _minuteCountFt : _engine.PersonalEmaFt;

                _engine.PerformDeepAnalysis(
                    _keyTimes.Count,
                    _mouseTimes.Count,
                    _backspaceTimes.Count,
                    _jerkTimes.Count,
                    _contextSwitchTimes.Count,
                    avgDt,
                    avgFt
                );

                UpdateHistory();
                ResetMinuteCounters();
                _tickCounter = 0;
            }

            UpdateTrayTooltip();
        }

        #region 데이터 수집 (Hook Events)
        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
        {
            DateTime now = DateTime.Now;
            lock (_keyTimes)
            {
                if (_pressedKeys.ContainsKey(e.Data.KeyCode)) return;

                _keyCount++;
                _engine.TotalAccumulatedKeys++; // 엔진의 누적 카운트 증가

                if (e.Data.KeyCode == SharpHook.Data.KeyCode.VcBackspace)
                {
                    _backspaceCount++;
                    lock (_backspaceTimes) { _backspaceTimes.Enqueue(now); }
                }

                _keyTimes.Enqueue(now);

                if (_lastKeyReleaseTime != DateTime.MinValue)
                {
                    double ft = (now - _lastKeyReleaseTime).TotalMilliseconds;
                    if (ft < 2000) { _minuteTotalFt += ft; _minuteCountFt++; }
                }
                _pressedKeys[e.Data.KeyCode] = now;
            }
        }

        private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
        {
            DateTime now = DateTime.Now;
            lock (_keyTimes)
            {
                _lastKeyReleaseTime = now;
                if (_pressedKeys.TryGetValue(e.Data.KeyCode, out DateTime pressTime))
                {
                    double dt = (now - pressTime).TotalMilliseconds;
                    if (dt < 1000) { _minuteTotalDt += dt; _minuteCountDt++; }
                    _pressedKeys.Remove(e.Data.KeyCode);
                }
            }
        }

        private void OnMousePressed(object? sender, MouseHookEventArgs e)
        {
            _mouseCount++;
            lock (_mouseTimes) { _mouseTimes.Enqueue(DateTime.Now); }
        }

        private void OnMouseMoved(object? sender, MouseHookEventArgs e)
        {
            DateTime now = DateTime.Now;
            if (_lastMouseX == -1) { _lastMouseX = e.Data.X; _lastMouseY = e.Data.Y; _lastMouseTime = now; return; }

            int dx = e.Data.X - _lastMouseX;
            int dy = e.Data.Y - _lastMouseY;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance > 100)
            {
                double timeElapsed = (now - _lastMouseTime).TotalSeconds;
                if (timeElapsed > 0 && (distance / timeElapsed) > 1000)
                {
                    double currentAngle = Math.Atan2(dy, dx) * (180 / Math.PI);
                    if (_lastMouseAngle != -1000)
                    {
                        double angleDiff = Math.Abs(currentAngle - _lastMouseAngle);
                        if (angleDiff > 180) angleDiff = 360 - angleDiff;

                        if (angleDiff > 135)
                        {
                            _mouseTurnTimes.Enqueue(now);
                            while (_mouseTurnTimes.Count > 0 && (now - _mouseTurnTimes.Peek()).TotalSeconds > 2) _mouseTurnTimes.Dequeue();
                            if (_mouseTurnTimes.Count >= 3) { lock (_jerkTimes) { _jerkTimes.Enqueue(now); } _mouseTurnTimes.Clear(); }
                        }
                    }
                    _lastMouseAngle = currentAngle;
                }
            }
            _lastMouseX = e.Data.X; _lastMouseY = e.Data.Y; _lastMouseTime = now;
        }
        #endregion

        #region 내부 헬퍼 메서드
        private void CleanOldData(DateTime now)
        {
            lock (_keyTimes) { while (_keyTimes.Count > 0 && (now - _keyTimes.Peek()).TotalSeconds > 60) _keyTimes.Dequeue(); }
            lock (_mouseTimes) { while (_mouseTimes.Count > 0 && (now - _mouseTimes.Peek()).TotalSeconds > 60) _mouseTimes.Dequeue(); }
            lock (_backspaceTimes) { while (_backspaceTimes.Count > 0 && (now - _backspaceTimes.Peek()).TotalSeconds > 60) _backspaceTimes.Dequeue(); }
            lock (_contextSwitchTimes) { while (_contextSwitchTimes.Count > 0 && (now - _contextSwitchTimes.Peek()).TotalSeconds > 60) _contextSwitchTimes.Dequeue(); }
            lock (_jerkTimes) { while (_jerkTimes.Count > 0 && (now - _jerkTimes.Peek()).TotalSeconds > 60) _jerkTimes.Dequeue(); }
        }

        private void MonitorWindowSwitch(DateTime now)
        {
            IntPtr currentWindow = GetForegroundWindow();
            if (currentWindow != _lastWindowHandle && currentWindow != IntPtr.Zero)
            {
                StringBuilder title = new StringBuilder(256);
                if (GetWindowText(currentWindow, title, 256) > 0)
                {
                    lock (_contextSwitchTimes) { _contextSwitchTimes.Enqueue(now); }
                }
                _lastWindowHandle = currentWindow;
            }
        }

        private void UpdateHistory()
        {
            _historyScores.Add(_engine.FocusScore);
            _historyStates.Add(_engine.FocusState);
            if (_historyScores.Count > 10) { _historyScores.RemoveAt(0); _historyStates.RemoveAt(0); }
        }

        private void ResetMinuteCounters()
        {
            _minuteTotalDt = 0; _minuteCountDt = 0;
            _minuteTotalFt = 0; _minuteCountFt = 0;
        }

        private void UpdateTrayTooltip()
        {
            if (_notifyIcon == null) return;
            if (!_engine.IsFirstAnalysisComplete) _notifyIcon.ToolTipText = $"⏳ 집중 패턴 분석 중...\n({60 - _tickCounter}초 남음)";
            else
            {
                string[] stateNames = { "Idle ☕", "Distracted 😵‍💫", "Engaged 🙂", "Focused 🤓", "Deep Focus 🔥" };
                string stateText = stateNames[Math.Clamp(_engine.FocusState, 0, 4)];
                _notifyIcon.ToolTipText = $"🎯 상태: {stateText} (집중도: {_engine.FocusScore}%)\n🔥 KPM: {_keyTimes.Count} | 🔀 창 전환: {_contextSwitchTimes.Count}회";
            }
        }
        #endregion

        #region Public Getters (Dashboard 연동용 - 전체 구현)
        public bool GetIsFirstAnalysisComplete() => _engine.IsFirstAnalysisComplete;
        public int GetRemainingSeconds() => 60 - _tickCounter;
        public int GetFocus() => _engine.FocusScore;
        public int GetStress() => _engine.StressScore;
        public int GetFatigue() => 0; 
        public int GetCurrentKPM() => _keyTimes.Count;
        public int GetCurrentMPM() => _mouseTimes.Count;
        public int GetCurrentAPM() => _keyTimes.Count + _mouseTimes.Count;
        public int GetBackspaceCount() => _backspaceTimes.Count;
        public int GetJerkCount() => _jerkTimes.Count;
        public int GetContextSwitchCount() => _contextSwitchTimes.Count;
        public int GetFocusState() => _engine.FocusState;
        public string GetStateReason() => _engine.StateReason;
        public List<int> GetHistoryScores() => new List<int>(_historyScores);
        public List<int> GetHistoryStates() => new List<int>(_historyStates);
        public double GetCurrentDt() => _minuteCountDt > 0 ? _minuteTotalDt / _minuteCountDt : _engine.PersonalEmaDt;
        public double GetCurrentFt() => _minuteCountFt > 0 ? _minuteTotalFt / _minuteCountFt : _engine.PersonalEmaFt;
        #endregion

        #region 시스템 설정 및 저장
        private void SetupNotifyIcon()
        {
            // (기존 아이콘 설정 코드와 동일...)
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (!File.Exists(iconPath)) return;

            _notifyIcon = new TaskbarIcon();
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.UriSource = new Uri(iconPath, UriKind.Absolute); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit();
            _notifyIcon.IconSource = bitmap; _notifyIcon.Visibility = Visibility.Visible; _notifyIcon.ForceCreate();

            // 컨텍스트 메뉴 설정
            var contextMenu = new System.Windows.Controls.ContextMenu();
            var detailsMenuItem = new System.Windows.Controls.MenuItem { Header = "📊 대시보드 자세히 보기", FontWeight = FontWeights.Bold };
            detailsMenuItem.Click += (s, ev) => ShowDashboard();
            contextMenu.Items.Add(detailsMenuItem);
            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var exitMenuItem = new System.Windows.Controls.MenuItem { Header = "프로그램 종료" };
            exitMenuItem.Click += (s, ev) => Current.Shutdown();
            contextMenu.Items.Add(exitMenuItem);

            _notifyIcon.ContextMenu = contextMenu;
            _notifyIcon.TrayLeftMouseDoubleClick += (s, ev) => ShowDashboard();
        }

        private void SetupGlobalHook()
        {
            _globalHook = new TaskPoolGlobalHook();
            _globalHook.KeyPressed += OnKeyPressed;
            _globalHook.KeyReleased += OnKeyReleased;
            _globalHook.MousePressed += OnMousePressed;
            _globalHook.MouseMoved += OnMouseMoved;
            _globalHook.RunAsync();
        }

        private void LoadUserData()
        {
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "userData.json");
                if (File.Exists(filePath))
                {
                    var data = JsonSerializer.Deserialize<UserData>(File.ReadAllText(filePath));
                    if (data != null)
                    {
                        _keyCount = data.KeyCount; _mouseCount = data.MouseCount; _backspaceCount = data.BackspaceCount;
                        _engine.PersonalEmaKpm = data.PersonalEmaKpm; _engine.PersonalEmaEr = data.PersonalEmaEr;
                        _engine.TotalAccumulatedKeys = data.TotalAccumulatedKeys;
                        _engine.PersonalVarKpm = data.PersonalVarKpm; _engine.PersonalVarEr = data.PersonalVarEr;
                        _engine.PersonalEmaDt = data.PersonalEmaDt; _engine.PersonalEmaFt = data.PersonalEmaFt;
                        _engine.PersonalVarDt = data.PersonalVarDt; _engine.PersonalVarFt = data.PersonalVarFt;
                        _engine.PersonalEmaMj = data.PersonalEmaMj; _engine.PersonalVarMj = data.PersonalVarMj;
                    }
                }
            }
            catch { }
        }

        private void SaveUserData()
        {
            try
            {
                var data = new UserData
                {
                    KeyCount = _keyCount,
                    MouseCount = _mouseCount,
                    BackspaceCount = _backspaceCount,
                    PersonalEmaKpm = _engine.PersonalEmaKpm,
                    PersonalEmaEr = _engine.PersonalEmaEr,
                    TotalAccumulatedKeys = _engine.TotalAccumulatedKeys,
                    PersonalVarKpm = _engine.PersonalVarKpm,
                    PersonalVarEr = _engine.PersonalVarEr,
                    PersonalEmaDt = _engine.PersonalEmaDt,
                    PersonalEmaFt = _engine.PersonalEmaFt,
                    PersonalVarDt = _engine.PersonalVarDt,
                    PersonalVarFt = _engine.PersonalVarFt,
                    PersonalEmaMj = _engine.PersonalEmaMj,
                    PersonalVarMj = _engine.PersonalVarMj
                };
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "userData.json"), JsonSerializer.Serialize(data));
            }
            catch { }
        }

        private void ShowDashboard()
        {
            if (_dashboardWindow == null) _dashboardWindow = new DashboardWindow(this);
            _dashboardWindow.Show(); _dashboardWindow.Activate();
        }
        #endregion

        public class UserData
        {
            public int KeyCount { get; set; }
            public int MouseCount { get; set; }
            public int BackspaceCount { get; set; }
            public double PersonalEmaKpm { get; set; }
            public double PersonalEmaEr { get; set; }
            public int TotalAccumulatedKeys { get; set; }
            public double PersonalVarKpm { get; set; }
            public double PersonalVarEr { get; set; }
            public double PersonalEmaDt { get; set; }
            public double PersonalEmaFt { get; set; }
            public double PersonalVarDt { get; set; }
            public double PersonalVarFt { get; set; }
            public double PersonalEmaMj { get; set; }
            public double PersonalVarMj { get; set; }
        }
    }
}