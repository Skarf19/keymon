using System;
using System.Collections.Generic;
using System.Windows.Threading;
using System.Windows;
using Microsoft.Win32;

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
        private readonly PersistenceService _persistence;

        private DispatcherTimer? _timer;
        private int _tickCounter;
        private DateTime _inactiveStartTime;
        private readonly List<int> _historyScores = new();
        private readonly List<int> _historyStates = new();
        private readonly List<int> _historyFatigue = new();

        private MetricSnapshot _lastSnapshot = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

        public MonitoringService(MetricCollector collector, AnalysisEngine engine, UnityBridge unity, TrayIconManager tray, PersistenceService persistence)
        {
            _collector = collector;
            _engine = engine;
            _unity = unity;
            _tray = tray;
            _persistence = persistence;

            _persistence.Load(_engine, _collector);
        }

        public void Start()
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public void Stop()
        {
            _timer?.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _persistence.Save(_engine, _collector);
        }

        // --- 절전 모드 대응 로직 ---
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) HandleSystemInactive();
            else if (e.Mode == PowerModes.Resume) HandleSystemActive();
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) HandleSystemInactive();
            else if (e.Reason == SessionSwitchReason.SessionUnlock) HandleSystemActive();
        }

        // 자리 비움 (절전, 잠금): 시간을 멈추고 현재 상태를 보존
        private void HandleSystemInactive()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _timer?.Stop();
                _inactiveStartTime = DateTime.Now;
                _persistence.Save(_engine, _collector);
            });
        }

        // 복귀 (깨어남, 잠금 해제): 멈췄던 시간을 그대로 다시 재생
        private void HandleSystemActive()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_inactiveStartTime != default)
                {
                    TimeSpan sleepDuration = DateTime.Now - _inactiveStartTime;
                    _collector.OffsetTime(sleepDuration);
                    _inactiveStartTime = default;
                }
                _timer?.Start();
            });
        }

        public void Reset()
        {
            _tickCounter = 0;
            _historyScores.Clear();
            _historyStates.Clear();
            _historyFatigue.Clear();

            _lastSnapshot = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
            _collector.Reset();
            _engine.Reset();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.Now;

            _collector.Tick(now);
            _lastSnapshot = _collector.GetSnapshot();

            int currentApm = _lastSnapshot.Kpm + _lastSnapshot.Mpm + _lastSnapshot.ScrollCount;

            if (_engine.IsStandby)
            {
                if (currentApm > 0)
                {
                    _engine.WakeUp();
                    _tickCounter = 0;
                    _collector.ResetTimingAccumulators();
                }
                else
                {
                    UpdateTrayTooltip();
                    _unity.SendState(_engine.FocusState);
                    return;
                }
            }

            _tickCounter++;

            _engine.UpdateRealtimeStatus(
                _lastSnapshot.Kpm,
                _lastSnapshot.Mpm + _lastSnapshot.ScrollCount,
                _lastSnapshot.ContextSwitchCount,
                _engine.IsFirstAnalysisComplete
            );

            if (_tickCounter >= 60)
            {
                _engine.TotalAccumulatedKeys = _collector.TotalAccumulatedKeys;

                double avgDt = _lastSnapshot.AvgDwellTime > 0 ? _lastSnapshot.AvgDwellTime : _engine.PersonalEmaDt;
                double avgFt = _lastSnapshot.AvgFlightTime > 0 ? _lastSnapshot.AvgFlightTime : _engine.PersonalEmaFt;

                _engine.PerformDeepAnalysis(
                    _lastSnapshot.Kpm,
                    _lastSnapshot.Mpm + _lastSnapshot.ScrollCount,
                    _lastSnapshot.BackspaceCount,
                    _lastSnapshot.MaxConsecutiveBackspaces,
                    _lastSnapshot.JerkCount,
                    _lastSnapshot.ContextSwitchCount,
                    avgDt,
                    avgFt
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
            _tray.IsStandby = _engine.IsStandby;

            if (_engine.IsStandby)
            {
                _tray.UpdateTooltip("대기 모드 (수집 일시정지)");
                return;
            }

            if (!_engine.IsFirstAnalysisComplete)
            {
                _tray.UpdateTooltip($"패턴 분석 중... ({60 - _tickCounter}초)");
                return;
            }
            string[] stateNames = { "Idle", "Distracted", "Engaged", "Focused", "Deep Focus" };
            string stateText = stateNames[Math.Clamp(_engine.FocusState, 0, 4)];
            _tray.UpdateTooltip($"{stateText} ({_engine.FocusScore}%)\nKPM: {_lastSnapshot.Kpm} | 창 전환: {_lastSnapshot.ContextSwitchCount}회");
            _tray.UpdateAnimationByState(_engine.FocusState);
        }

        // ── ISessionData 구현부 (변경 없음) ──────────────────────────────────────
        public bool IsFirstAnalysisComplete => _engine.IsFirstAnalysisComplete;
        public int RemainingSeconds => 60 - _tickCounter;
        public int FocusScore => _engine.FocusScore;
        public int StressScore => _engine.StressScore;
        public double FatigueScore => _engine.FatigueScore;
        public int CurrentKpm => _lastSnapshot.Kpm;
        public int CurrentMpm => _lastSnapshot.Mpm;
        public int CurrentApm => _lastSnapshot.Kpm + _lastSnapshot.Mpm + _lastSnapshot.ScrollCount;
        public int BackspaceCount => _lastSnapshot.BackspaceCount;
        public int JerkCount => _lastSnapshot.JerkCount;
        public int ContextSwitchCount => _lastSnapshot.ContextSwitchCount;
        public int FocusState => _engine.FocusState;
        public int FatigueState => _engine.FatigueState;
        public string StateReason => _engine.StateReason;
        public List<int> HistoryScores => new List<int>(_historyScores);
        public List<int> HistoryFatigue => new List<int>(_historyFatigue);

        public bool IsStandby => _engine.IsStandby;
    }
}