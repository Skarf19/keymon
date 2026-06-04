using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
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
        private int _totalSessionTicks = 0;
        private DateTime _inactiveStartTime;
        private readonly List<int> _historyScores = new();
        private readonly List<int> _historyStates = new();
        private readonly List<int> _historyFatigue = new();

        private readonly Dictionary<string, DailyStat> _dailyStats = new();
        public Dictionary<string, DailyStat> DailyStats => new Dictionary<string, DailyStat>(_dailyStats);

        private MetricSnapshot _lastSnapshot = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

        public bool IsManualStandby { get; private set; } = false;

        public MonitoringService(MetricCollector collector, AnalysisEngine engine, UnityBridge unity, TrayIconManager tray, PersistenceService persistence)
        {
            _collector = collector;
            _engine = engine;
            _unity = unity;
            _tray = tray;
            _persistence = persistence;

            _persistence.Load(_engine, _collector, this);

            // 로드 직후 과거 데이터가 있다면, 시작하자마자 60초를 채운 것으로 간주!
            if (_engine.IsFirstAnalysisComplete || _historyScores.Count > 0)
            {
                _engine.IsFirstAnalysisComplete = true;
                _tickCounter = 60;
            }
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
            _persistence.Save(_engine, _collector, this);
        }

        public void ToggleManualStandby()
        {
            IsManualStandby = !IsManualStandby;
            if (IsManualStandby)
            {
                _collector.ResetTimingAccumulators();
            }
            else
            {
                _engine.WakeUp();
                _tickCounter = 0;
            }
        }

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

        private void HandleSystemInactive()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _timer?.Stop();
                _inactiveStartTime = DateTime.Now;
                _persistence.Save(_engine, _collector, this);
            });
        }

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
            _totalSessionTicks = 0;
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

            if (IsManualStandby)
            {
                UpdateDailyStat(isStandby: true);
                UpdateTrayTooltip();
                _unity.SendState(0);
                return;
            }

            _lastSnapshot = _collector.GetSnapshot();
            int currentApm = _lastSnapshot.Kpm + _lastSnapshot.Mpm + _lastSnapshot.ScrollCount;

            // 앱이 켜진 후 흐른 총 시간(초)
            _totalSessionTicks++;

            bool isWarmUpPeriod = _totalSessionTicks <= 300;

            // 5분이 지나기 전까지는 엔진이 '대기(Standby)'라고 판정해도 무시하고 예전 상태를 유지합니다.
            if (_engine.IsStandby && !isWarmUpPeriod)
            {
                if (currentApm > 0)
                {
                    _engine.WakeUp();
                    _tickCounter = 0;
                    _collector.ResetTimingAccumulators();
                }
                else
                {
                    _tickCounter++;
                    if (_tickCounter >= 60)
                    {
                        UpdateDailyStat(isStandby: true);
                        _tickCounter = 0;
                    }
                    UpdateTrayTooltip();
                    _unity.SendState(_engine.FocusState);
                    return;
                }
            }

            _tickCounter++;

            // 실시간 상태 하락(Drop) 역시 5분(300틱) 동안은 절대 발생하지 않도록 방어합니다.
            bool isSafeToDrop = _engine.IsFirstAnalysisComplete && !isWarmUpPeriod;

            _engine.UpdateRealtimeStatus(
                _lastSnapshot.Kpm,
                _lastSnapshot.Mpm + _lastSnapshot.ScrollCount,
                _lastSnapshot.ContextSwitchCount,
                isSafeToDrop
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

                _engine.IsFirstAnalysisComplete = true;

                UpdateHistory();
                UpdateDailyStat(isStandby: false);
                _collector.ResetTimingAccumulators();
                _tickCounter = 0;
            }

            UpdateTrayTooltip();
            _unity.SendState(_engine.FocusState);
        }

        private void UpdateDailyStat(bool isStandby = false)
        {
            try
            {
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                int hour = DateTime.Now.Hour;

                if (!_dailyStats.ContainsKey(today))
                    _dailyStats[today] = new DailyStat { DateString = today };

                var stat = _dailyStats[today];
                stat.StateCounts ??= new int[5];
                stat.HourlyMinutes ??= new int[24];
                stat.HourlyActiveMinutes ??= new int[24];

                stat.TotalMinutes++;
                stat.HourlyMinutes[hour]++;

                if (isStandby)
                {
                    stat.StateCounts[0]++;
                }
                else
                {
                    stat.TotalActiveMinutes++;
                    stat.TotalFocusSum += _engine.FocusScore;
                    stat.TotalFatigueSum += (int)_engine.FatigueScore;

                    int stateIdx = Math.Clamp(_engine.FocusState, 0, 4);
                    stat.StateCounts[stateIdx]++;

                    stat.HourlyFocusSum[hour] += _engine.FocusScore;
                    stat.HourlyFatigueSum[hour] += (int)_engine.FatigueScore;
                    stat.HourlyActiveMinutes[hour]++;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
        }

        private void UpdateHistory()
        {
            _historyScores.Add(_engine.FocusScore);
            _historyStates.Add(_engine.FocusState);
            _historyFatigue.Add((int)_engine.FatigueScore);

            if (_historyScores.Count > 60)
            {
                _historyScores.RemoveAt(0);
                _historyStates.RemoveAt(0);
                _historyFatigue.RemoveAt(0);
            }
        }

        public void RestoreHistory(List<int> scores, List<int> states, List<int> fatigue)
        {
            if (scores != null) { _historyScores.Clear(); _historyScores.AddRange(scores); }
            if (states != null) { _historyStates.Clear(); _historyStates.AddRange(states); }
            if (fatigue != null) { _historyFatigue.Clear(); _historyFatigue.AddRange(fatigue); }
        }

        public (List<int> scores, List<int> states, List<int> fatigue) GetHistoryForSave()
        {
            return (_historyScores, _historyStates, _historyFatigue);
        }

        public void RestoreDailyStats(Dictionary<string, DailyStat> stats)
        {
            if (stats != null)
            {
                _dailyStats.Clear();
                foreach (var kvp in stats) _dailyStats[kvp.Key] = kvp.Value;
            }
        }

        private void UpdateTrayTooltip()
        {
            if (IsManualStandby)
            {
                _tray.IsStandby = true;
                _tray.UpdateTooltip("⏸️ 모니터링 일시 정지 (수동 대기)");
                return;
            }

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