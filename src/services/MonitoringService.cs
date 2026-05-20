using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Microsoft.Win32;            // 절전 모드 대응

namespace Keymon
{
    // MonitoringService의 역할:
    //   - 1초마다 실행되는 타이머 루프 소유
    //   - 절전 모드 진입/해제 감지 및 대응
    //   - PersistenceService를 통한 데이터 Save/Load 트리거
    public class MonitoringService : ISessionData
    {
        private readonly MetricCollector _collector;
        private readonly AnalysisEngine _engine;
        private readonly UnityBridge _unity;
        private readonly TrayIconManager _tray;
        private readonly PersistenceService _persistence; // 분리된 영속성 서비스

        private DispatcherTimer? _timer;
        private int _tickCounter;
        private readonly List<int> _historyScores = new();
        private readonly List<int> _historyStates = new();
        private readonly List<int> _historyFatigue = new();
        private MetricSnapshot _lastSnapshot = new(0, 0, 0, 0, 0, 0, 0);

        // 생성자: 모든 의존성을 주입받고 초기 데이터를 불러옵니다.
        public MonitoringService(MetricCollector collector, AnalysisEngine engine, UnityBridge unity, TrayIconManager tray, PersistenceService persistence)
        {
            _collector = collector;
            _engine = engine;
            _unity = unity;
            _tray = tray;
            _persistence = persistence;

            // 서비스 생성 시 자동으로 데이터 복원
            _persistence.Load(_engine, _collector);
        }

        public void Start()
        {
            // 전원 상태 변경 감지 구독
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public void Stop()
        {
            _timer?.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;

            // 앱 종료 전 안전하게 데이터 저장
            _persistence.Save(_engine, _collector);
        }

        // --- 절전 모드 대응 로직 ---
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) // 덮개를 닫거나 절전모드 진입 시
            {
                _timer?.Stop();
                _persistence.Save(_engine, _collector); // 중간 저장
            }
            else if (e.Mode == PowerModes.Resume) // 덮개를 열거나 깨어날 시
            {
                _tickCounter = 0; // 타이머 꼬임 방지
                _timer?.Start();
            }
        }

        public void Reset()
        {
            _tickCounter = 0;
            _historyScores.Clear();
            _historyStates.Clear();
            _historyFatigue.Clear();
            _lastSnapshot = new(0, 0, 0, 0, 0, 0, 0);
            _collector.Reset();
            _engine.Reset();
        }

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
            _historyFatigue.Add((int)_engine.FatigueScore);

            if (_historyScores.Count > 10)
            {
                _historyScores.RemoveAt(0);
                _historyStates.RemoveAt(0);
                _historyFatigue.RemoveAt(0);
            }
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
        public double FatigueScore => _engine.FatigueScore;
        public int CurrentKpm => _lastSnapshot.Kpm;
        public int CurrentMpm => _lastSnapshot.Mpm;
        public int CurrentApm => _lastSnapshot.Kpm + _lastSnapshot.Mpm;
        public int BackspaceCount => _lastSnapshot.BackspaceCount;
        public int JerkCount => _lastSnapshot.JerkCount;
        public int ContextSwitchCount => _lastSnapshot.ContextSwitchCount;
        public int FocusState => _engine.FocusState;
        public int FatigueState => _engine.FatigueState;
        public string StateReason => _engine.StateReason;
        public List<int> HistoryScores => new List<int>(_historyScores);
        public List<int> HistoryFatigue => new List<int>(_historyFatigue);
    }
}