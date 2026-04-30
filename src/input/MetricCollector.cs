using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using SharpHook;
using SharpHook.Data;

namespace Keymon
{
    // MetricCollector의 역할:
    //   - 키보드/마우스 이벤트를 실시간으로 받아서 60초 롤링 큐에 저장합니다.
    //   - 윈도우 전환을 감지하여 컨텍스트 스위치 큐에 저장합니다.
    //   - GetSnapshot()을 통해 현재 지표를 MetricSnapshot으로 묶어 외부에 제공합니다.
    //
    // 이전에는 이 모든 로직이 App.xaml.cs에 있었습니다.
    // 이제 이 클래스 하나가 '입력 수집' 책임만 집니다.
    public class MetricCollector
    {
        // Queue<DateTime>은 선입선출(FIFO) 자료구조입니다.
        // 이벤트 발생 시각을 순서대로 저장하고, 60초가 지난 항목은 앞에서 제거합니다.
        // 즉, Count가 곧 '최근 60초 내 발생 횟수'가 됩니다.
        private readonly Queue<DateTime> _keyTimes = new();
        private readonly Queue<DateTime> _mouseTimes = new();
        private readonly Queue<DateTime> _backspaceTimes = new();
        private readonly Queue<DateTime> _contextSwitchTimes = new();
        private readonly Queue<DateTime> _jerkTimes = new();

        // 마우스 방향 급전환(Jerk) 감지를 위한 임시 큐입니다.
        // 2초 내에 3회 이상 급전환이 있을 때만 _jerkTimes에 기록합니다.
        private readonly Queue<DateTime> _mouseTurnTimes = new();

        // Dictionary<TKey, TValue>는 키-값 쌍의 자료구조입니다 (파이썬의 dict와 동일).
        // 현재 눌린 키와 그 누른 시각을 저장해 Dwell Time(키 누름 지속 시간)을 계산합니다.
        private readonly Dictionary<KeyCode, DateTime> _pressedKeys = new();

        // 이번 분기(60초) 동안의 Dwell Time / Flight Time 누적값
        private double _minuteTotalDt;
        private int _minuteCountDt;
        private double _minuteTotalFt;
        private int _minuteCountFt;

        private DateTime _lastKeyReleaseTime = DateTime.MinValue;

        // 마우스 Jerk 계산을 위한 이전 위치/각도/시간 저장
        private int _lastMouseX = -1;
        private int _lastMouseY = -1;
        private double _lastMouseAngle = -1000;
        private DateTime _lastMouseTime = DateTime.MinValue;

        // 창 전환 감지를 위한 이전 포어그라운드 윈도우 핸들
        private IntPtr _lastWindowHandle = IntPtr.Zero;

        // AnalysisEngine의 currentGamma 계산에 사용되는 총 누적 키 입력 수
        // { get; set; }는 외부에서 읽고 쓸 수 있는 속성입니다.
        public int TotalAccumulatedKeys { get; set; }

        // persistence용 총 카운터 (분석에는 사용되지 않고 userData.json에 저장만 됨)
        public int TotalKeyCount { get; set; }
        public int TotalMouseCount { get; set; }
        public int TotalBackspaceCount { get; set; }

        // P/Invoke: C#에서 Windows API(C언어로 작성된 함수)를 직접 호출하는 방법입니다.
        // user32.dll은 Windows UI 관련 기능을 담은 시스템 라이브러리입니다.
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        // InputHookManager의 이벤트에 이 클래스의 핸들러를 연결합니다.
        // '+=' 는 이벤트 구독(subscribe)을 의미합니다. 이벤트가 발생하면 핸들러가 자동 호출됩니다.
        public void Subscribe(InputHookManager hookManager)
        {
            hookManager.KeyPressed += OnKeyPressed;
            hookManager.KeyReleased += OnKeyReleased;
            hookManager.MousePressed += OnMousePressed;
            hookManager.MouseMoved += OnMouseMoved;
        }

        // MonitoringService가 매 1초마다 호출합니다.
        // 오래된 데이터 정리 + 창 전환 감지를 수행합니다.
        public void Tick(DateTime now)
        {
            CleanOldData(now);
            MonitorWindowSwitch(now);
        }

        // 현재 수집된 지표들을 불변 스냅샷으로 패키징하여 반환합니다.
        // 호출자는 이 스냅샷을 통해 데이터를 읽기만 할 수 있고, 내부 큐를 건드릴 수 없습니다.
        public MetricSnapshot GetSnapshot()
        {
            double avgDt = _minuteCountDt > 0 ? _minuteTotalDt / _minuteCountDt : 0;
            double avgFt = _minuteCountFt > 0 ? _minuteTotalFt / _minuteCountFt : 0;

            return new MetricSnapshot(
                Kpm: _keyTimes.Count,
                Mpm: _mouseTimes.Count,
                BackspaceCount: _backspaceTimes.Count,
                JerkCount: _jerkTimes.Count,
                ContextSwitchCount: _contextSwitchTimes.Count,
                AvgDwellTime: avgDt,
                AvgFlightTime: avgFt
            );
        }

        // 60초 심층 분석 후 MonitoringService가 호출합니다.
        // 다음 분기를 위해 누적값을 초기화합니다.
        public void ResetTimingAccumulators()
        {
            _minuteTotalDt = 0;
            _minuteCountDt = 0;
            _minuteTotalFt = 0;
            _minuteCountFt = 0;
        }

        // 사용자가 트레이에서 '데이터 초기화'를 선택했을 때 호출됩니다.
        // 모든 수집 데이터와 누적값을 초기 상태로 되돌립니다.
        public void Reset()
        {
            TotalAccumulatedKeys = 0;
            TotalKeyCount = 0;
            TotalMouseCount = 0;
            TotalBackspaceCount = 0;
            _lastKeyReleaseTime = DateTime.MinValue;
            _lastWindowHandle = IntPtr.Zero;
            _pressedKeys.Clear();

            // lock: 여러 스레드가 동시에 같은 자원에 접근하는 것을 막습니다.
            // SharpHook은 백그라운드 스레드에서 이벤트를 발생시키므로,
            // Reset 중에 이벤트가 들어오면 데이터가 꼬일 수 있어 lock이 필요합니다.
            lock (_keyTimes) { _keyTimes.Clear(); }
            lock (_mouseTimes) { _mouseTimes.Clear(); }
            lock (_backspaceTimes) { _backspaceTimes.Clear(); }
            lock (_contextSwitchTimes) { _contextSwitchTimes.Clear(); }
            lock (_jerkTimes) { _jerkTimes.Clear(); }
            _mouseTurnTimes.Clear();

            ResetTimingAccumulators();
        }

        // SharpHook 백그라운드 스레드에서 호출됩니다 (키가 눌릴 때마다).
        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
        {
            DateTime now = DateTime.Now;
            var keyCode = e.Data.KeyCode;

            lock (_keyTimes)
            {
                // 키 반복 입력(키 누른 채 유지) 방지: 이미 눌린 키면 무시
                if (_pressedKeys.ContainsKey(keyCode)) return;

                TotalAccumulatedKeys++;
                TotalKeyCount++;

                if (keyCode == KeyCode.VcBackspace)
                {
                    TotalBackspaceCount++;
                    lock (_backspaceTimes) { _backspaceTimes.Enqueue(now); }
                }

                _keyTimes.Enqueue(now);

                // Flight Time: 이전 키를 뗀 시각부터 이번 키를 누른 시각까지의 간격
                if (_lastKeyReleaseTime != DateTime.MinValue)
                {
                    double ft = (now - _lastKeyReleaseTime).TotalMilliseconds;
                    if (ft < 2000) { _minuteTotalFt += ft; _minuteCountFt++; }
                }

                _pressedKeys[keyCode] = now;
            }
        }

        // 키가 떼어질 때마다 호출됩니다.
        private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
        {
            DateTime now = DateTime.Now;
            var keyCode = e.Data.KeyCode;

            lock (_keyTimes)
            {
                _lastKeyReleaseTime = now;

                // TryGetValue: Dictionary에서 키를 찾아 값을 가져옵니다. 없으면 false 반환.
                if (_pressedKeys.TryGetValue(keyCode, out DateTime pressTime))
                {
                    // Dwell Time: 키를 누른 시각부터 뗀 시각까지의 지속 시간
                    double dt = (now - pressTime).TotalMilliseconds;
                    if (dt < 1000) { _minuteTotalDt += dt; _minuteCountDt++; }
                    _pressedKeys.Remove(keyCode);
                }
            }
        }

        // 마우스 버튼이 클릭될 때마다 호출됩니다.
        private void OnMousePressed(object? sender, MouseHookEventArgs e)
        {
            TotalMouseCount++;
            lock (_mouseTimes) { _mouseTimes.Enqueue(DateTime.Now); }
        }

        // 마우스가 움직일 때마다 호출됩니다 (Jerk 감지 로직).
        private void OnMouseMoved(object? sender, MouseHookEventArgs e)
        {
            DateTime now = DateTime.Now;

            if (_lastMouseX == -1)
            {
                _lastMouseX = e.Data.X;
                _lastMouseY = e.Data.Y;
                _lastMouseTime = now;
                return;
            }

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
                            while (_mouseTurnTimes.Count > 0 && (now - _mouseTurnTimes.Peek()).TotalSeconds > 2)
                                _mouseTurnTimes.Dequeue();
                            if (_mouseTurnTimes.Count >= 3)
                            {
                                lock (_jerkTimes) { _jerkTimes.Enqueue(now); }
                                _mouseTurnTimes.Clear();
                            }
                        }
                    }
                    _lastMouseAngle = currentAngle;
                }
            }

            _lastMouseX = e.Data.X;
            _lastMouseY = e.Data.Y;
            _lastMouseTime = now;
        }

        // 각 큐에서 60초가 지난 오래된 항목을 앞에서부터 제거합니다.
        private void CleanOldData(DateTime now)
        {
            lock (_keyTimes) { while (_keyTimes.Count > 0 && (now - _keyTimes.Peek()).TotalSeconds > 60) _keyTimes.Dequeue(); }
            lock (_mouseTimes) { while (_mouseTimes.Count > 0 && (now - _mouseTimes.Peek()).TotalSeconds > 60) _mouseTimes.Dequeue(); }
            lock (_backspaceTimes) { while (_backspaceTimes.Count > 0 && (now - _backspaceTimes.Peek()).TotalSeconds > 60) _backspaceTimes.Dequeue(); }
            lock (_contextSwitchTimes) { while (_contextSwitchTimes.Count > 0 && (now - _contextSwitchTimes.Peek()).TotalSeconds > 60) _contextSwitchTimes.Dequeue(); }
            lock (_jerkTimes) { while (_jerkTimes.Count > 0 && (now - _jerkTimes.Peek()).TotalSeconds > 60) _jerkTimes.Dequeue(); }
        }

        // Windows API로 현재 포어그라운드 창을 확인합니다.
        private void MonitorWindowSwitch(DateTime now)
        {
            IntPtr currentWindow = GetForegroundWindow();
            if (currentWindow != _lastWindowHandle && currentWindow != IntPtr.Zero)
            {
                StringBuilder title = new StringBuilder(256);
                if (GetWindowText(currentWindow, title, 256) > 0)
                    lock (_contextSwitchTimes) { _contextSwitchTimes.Enqueue(now); }
                _lastWindowHandle = currentWindow;
            }
        }
    }
}
