using H.NotifyIcon;
using Microsoft.Win32;
using SharpHook;
using SharpHook.Native; // 💡 KeyCode 인식을 위해 명시적으로 추가
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
        // 1. 핵심 모듈 (엔진과 후킹 매니저)
        private AnalysisEngine _engine = new AnalysisEngine();
        private InputHookManager _hookManager = new InputHookManager();

        private DashboardWindow? _dashboardWindow;
        private TaskbarIcon? _notifyIcon;
        private DispatcherTimer? _timer;

        // 2. 수집 데이터 (실시간 60초 슬라이딩 윈도우)
        private Queue<DateTime> _keyTimes = new Queue<DateTime>();
        private Queue<DateTime> _mouseTimes = new Queue<DateTime>();
        private Queue<DateTime> _backspaceTimes = new Queue<DateTime>();
        private Queue<DateTime> _contextSwitchTimes = new Queue<DateTime>();
        private Queue<DateTime> _jerkTimes = new Queue<DateTime>();
        private Queue<DateTime> _mouseTurnTimes = new Queue<DateTime>();

        // 3. 누적 및 히스토리 데이터
        private int _keyCount = 0;
        private int _mouseCount = 0;
        private int _backspaceCount = 0;
        private List<int> _historyScores = new List<int>();
        private List<int> _historyStates = new List<int>();
        private int _tickCounter = 0;

        // 4. 간격 측정용 (DT, FT)
        private double _minuteTotalDt = 0;
        private int _minuteCountDt = 0;
        private double _minuteTotalFt = 0;
        private int _minuteCountFt = 0;
        private DateTime _lastKeyReleaseTime = DateTime.MinValue;

        // 💡 Dictionary 키 타입을 명확히 지정
        private Dictionary<SharpHook.Native.KeyCode, DateTime> _pressedKeys = new Dictionary<SharpHook.Native.KeyCode, DateTime>();

        // 5. 마우스 궤적 계산용
        private int _lastMouseX = -1;
        private int _lastMouseY = -1;
        private double _lastMouseAngle = -1000;
        private DateTime _lastMouseTime = DateTime.MinValue;
        private IntPtr _lastWindowHandle = IntPtr.Zero;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            LoadUserData();     // 기존 학습 데이터 로드
            SetupNotifyIcon();  // 트레이 아이콘 설정

            // 후킹 매니저 연결 및 시작
            _hookManager.KeyPressed += OnKeyPressed;
            _hookManager.KeyReleased += OnKeyReleased;
            _hookManager.MousePressed += OnMousePressed;
            _hookManager.MouseMoved += OnMouseMoved;
            _hookManager.Start();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            CleanOldData(now); // 1분 지난 데이터 삭제

            _tickCounter++;
            MonitorWindowSwitch(now); // 창 전환 감시

            // 실시간 상태 판단 (엔진)
            _engine.UpdateRealtimeStatus(_keyTimes.Count, _mouseTimes.Count, _contextSwitchTimes.Count, _engine.IsFirstAnalysisComplete);

            // 60초 주기 심층 분석 (엔진)
            if (_tickCounter >= 60)
            {
                double avgDt = _minuteCountDt > 0 ? _minuteTotalDt / _minuteCountDt : _engine.PersonalEmaDt;
                double avgFt = _minuteCountFt > 0 ? _minuteTotalFt / _minuteCountFt : _engine.PersonalEmaFt;

                _engine.PerformDeepAnalysis(_keyTimes.Count, _mouseTimes.Count, _backspaceTimes.Count, _jerkTimes.Count, _contextSwitchTimes.Count, avgDt, avgFt);

                _historyScores.Add(_engine.FocusScore);
                _historyStates.Add(_engine.FocusState);
                if (_historyScores.Count > 10) { _historyScores.RemoveAt(0); _historyStates.RemoveAt(0); }

                _minuteTotalDt = 0; _minuteCountDt = 0; _minuteTotalFt = 0; _minuteCountFt = 0;
                _tickCounter = 0;
            }

            UpdateTrayTooltip();
        }

        #region 입력 이벤트 처리 (Event Handlers)
        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
        {
            DateTime now = DateTime.Now;

            // 💡 핵심 수정: SharpHook 버전 충돌을 방지하기 위한 강제 형변환
            var keyCode = (SharpHook.Native.KeyCode)e.Data.KeyCode;

            lock (_keyTimes)
            {
                if (_pressedKeys.ContainsKey(keyCode)) return;

                _keyCount++;
                _engine.TotalAccumulatedKeys++;

                // Backspace 감지 (명시적 타입 비교)
                if (keyCode == SharpHook.Native.KeyCode.VcBackspace)
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
                _pressedKeys[keyCode] = now;
            }
        }

        private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
        {
            DateTime now = DateTime.Now;

            // 💡 핵심 수정: 강제 형변환
            var keyCode = (SharpHook.Native.KeyCode)e.Data.KeyCode;

            lock (_keyTimes)
            {
                _lastKeyReleaseTime = now;
                if (_pressedKeys.TryGetValue(keyCode, out DateTime pressTime))
                {
                    double dt = (now - pressTime).TotalMilliseconds;
                    if (dt < 1000) { _minuteTotalDt += dt; _minuteCountDt++; }
                    _pressedKeys.Remove(keyCode);
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

        #region 유틸리티 메서드 (Helpers)
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
                if (GetWindowText(currentWindow, title, 256) > 0) { lock (_contextSwitchTimes) { _contextSwitchTimes.Enqueue(now); } }
                _lastWindowHandle = currentWindow;
            }
        }

        private void UpdateTrayTooltip()
        {
            if (_notifyIcon == null) return;
            if (!_engine.IsFirstAnalysisComplete) _notifyIcon.ToolTipText = $"⏳ 집중 패턴 분석 중... ({60 - _tickCounter}초)";
            else
            {
                string[] stateNames = { "Idle ☕", "Distracted 😵‍💫", "Engaged 🙂", "Focused 🤓", "Deep Focus 🔥" };
                _notifyIcon.ToolTipText = $"🎯 {stateNames[Math.Clamp(_engine.FocusState, 0, 4)]} ({_engine.FocusScore}%)\nKPM: {_keyTimes.Count} | 창 전환: {_contextSwitchTimes.Count}회";
            }
        }
        #endregion

        #region 데이터 로드 및 저장
        private void LoadUserData()
        {
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "userData.json");
                if (!File.Exists(filePath)) return;
                var data = JsonSerializer.Deserialize<UserData>(File.ReadAllText(filePath));
                if (data == null) return;

                _keyCount = data.KeyCount; _mouseCount = data.MouseCount; _backspaceCount = data.BackspaceCount;
                _engine.PersonalEmaKpm = data.PersonalEmaKpm; _engine.PersonalEmaEr = data.PersonalEmaEr;
                _engine.TotalAccumulatedKeys = data.TotalAccumulatedKeys;
                _engine.PersonalVarKpm = data.PersonalVarKpm; _engine.PersonalVarEr = data.PersonalVarEr;
                _engine.PersonalEmaDt = data.PersonalEmaDt; _engine.PersonalEmaFt = data.PersonalEmaFt;
                _engine.PersonalVarDt = data.PersonalVarDt; _engine.PersonalVarFt = data.PersonalVarFt;
                _engine.PersonalEmaMj = data.PersonalEmaMj; _engine.PersonalVarMj = data.PersonalVarMj;
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
        #endregion

        #region Dashboard 연동 Getters
        public bool GetIsFirstAnalysisComplete() => _engine.IsFirstAnalysisComplete;
        public int GetRemainingSeconds() => 60 - _tickCounter;
        public int GetFocus() => _engine.FocusScore;
        public int GetStress() => _engine.StressScore;
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

        private void SetupNotifyIcon() { /* 트레이 아이콘 설정 로직 */ }
        private void ShowDashboard() { if (_dashboardWindow == null) _dashboardWindow = new DashboardWindow(this); _dashboardWindow.Show(); }
        protected override void OnExit(ExitEventArgs e) { _hookManager.Stop(); SaveUserData(); base.OnExit(e); }

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