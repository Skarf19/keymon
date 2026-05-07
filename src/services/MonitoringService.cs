using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace Keymon
{
    // MonitoringService의 역할:
    //   - 1초마다 실행되는 타이머 루프를 소유합니다.
    //   - MetricCollector에서 스냅샷을 가져와 AnalysisEngine에 전달합니다.
    //   - 60초마다 심층 분석을 트리거합니다.
    //   - 분석 결과를 UnityBridge와 TrayIconManager에 전달합니다.
    //   - ISessionData를 구현하여 DashboardWindow에 데이터를 제공합니다.
    public class MonitoringService : ISessionData
    {
        private readonly MetricCollector _collector;
        private readonly AnalysisEngine _engine;
        private readonly UnityBridge _unity;
        private readonly TrayIconManager _tray;

        // DispatcherTimer: WPF에서 UI 스레드에서 주기적으로 코드를 실행하는 타이머입니다.
        private DispatcherTimer? _timer;

        private int _tickCounter;
        private readonly List<int> _historyScores = new();
        private readonly List<int> _historyStates = new();

        // 매 틱마다 갱신되는 최신 스냅샷을 캐시합니다.
        private MetricSnapshot _lastSnapshot = new(0, 0, 0, 0, 0, 0, 0);

        // 생성자: 필요한 의존 객체들을 외부에서 주입받습니다 (의존성 주입 패턴).
        public MonitoringService(MetricCollector collector, AnalysisEngine engine, UnityBridge unity, TrayIconManager tray)
        {
            _collector = collector;
            _engine = engine;
            _unity = unity;
            _tray = tray;
        }

        public void Start()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public void Stop() => _timer?.Stop();

        public void Reset()
        {
            _tickCounter = 0;
            _historyScores.Clear();
            _historyStates.Clear();
            _lastSnapshot = new(0, 0, 0, 0, 0, 0, 0);
            _collector.Reset();
            _engine.Reset();
        }

        // 1초마다 DispatcherTimer가 자동으로 호출합니다.
        private void OnTick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            _tickCounter++;

            _collector.Tick(now);
            _lastSnapshot = _collector.GetSnapshot();

            _engine.UpdateRealtimeStatus(
                _lastSnapshot.Kpm,
                _lastSnapshot.Mpm,
                _lastSnapshot.ContextSwitchCount,
                _engine.IsFirstAnalysisComplete
            );

            if (_tickCounter >= 60)
            {
                _engine.TotalAccumulatedKeys = _collector.TotalAccumulatedKeys;

                double avgDt = _lastSnapshot.AvgDwellTime > 0 ? _lastSnapshot.AvgDwellTime : _engine.PersonalEmaDt;
                double avgFt = _lastSnapshot.AvgFlightTime > 0 ? _lastSnapshot.AvgFlightTime : _engine.PersonalEmaFt;

                _engine.PerformDeepAnalysis(
                    _lastSnapshot.Kpm, _lastSnapshot.Mpm,
                    _lastSnapshot.BackspaceCount, _lastSnapshot.JerkCount,
                    _lastSnapshot.ContextSwitchCount, avgDt, avgFt
                );

                UpdateHistory();
                _collector.ResetTimingAccumulators();
                _tickCounter = 0;
            }

            UpdateTrayTooltip();
            _unity.SendState(_engine.FocusState);
        }

        private void UpdateHistory()
        {
            _historyScores.Add(_engine.FocusScore);
            _historyStates.Add(_engine.FocusState);
            if (_historyScores.Count > 10) { _historyScores.RemoveAt(0); _historyStates.RemoveAt(0); }
        }

        private void UpdateTrayTooltip()
        {
            if (!_engine.IsFirstAnalysisComplete)
            {
                _tray.UpdateTooltip($"⏳ 패턴 분석 중... ({60 - _tickCounter}초)");
                return;
            }
            string[] stateNames = { "Idle ☕", "Distracted 😵‍💫", "Engaged 🙂", "Focused 🤓", "Deep Focus 🔥" };
            string stateText = stateNames[Math.Clamp(_engine.FocusState, 0, 4)];
            _tray.UpdateTooltip($"🎯 {stateText} ({_engine.FocusScore}%)\nKPM: {_lastSnapshot.Kpm} | 창 전환: {_lastSnapshot.ContextSwitchCount}회");
            _tray.UpdateAnimationByState(_engine.FocusState);
        }

        // ── ISessionData 구현부 ──────────────────────────────────────────────────
        public bool IsFirstAnalysisComplete => _engine.IsFirstAnalysisComplete;
        public int RemainingSeconds => 60 - _tickCounter;
        public int FocusScore => _engine.FocusScore;
        public int StressScore => _engine.StressScore;
        public int CurrentKpm => _lastSnapshot.Kpm;
        public int CurrentMpm => _lastSnapshot.Mpm;
        public int CurrentApm => _lastSnapshot.Kpm + _lastSnapshot.Mpm;
        public int BackspaceCount => _lastSnapshot.BackspaceCount;
        public int JerkCount => _lastSnapshot.JerkCount;
        public int ContextSwitchCount => _lastSnapshot.ContextSwitchCount;
        public int FocusState => _engine.FocusState;
        public string StateReason => _engine.StateReason;
        public List<int> HistoryScores => new List<int>(_historyScores);
    }
}
