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
    //
    // 이전에는 이 모든 로직이 App.xaml.cs의 Timer_Tick 등에 흩어져 있었습니다.
    //
    // ': ISessionData'는 이 클래스가 ISessionData 인터페이스를 구현한다는 의미입니다.
    // 즉, ISessionData에 정의된 모든 속성을 반드시 이 클래스에서 구현해야 합니다.
    public class MonitoringService : ISessionData
    {
        // readonly: 생성자에서 한 번만 할당되고 이후로는 변경 불가능한 필드입니다.
        // 이렇게 하면 실수로 다른 곳에서 _collector를 교체하는 사고를 방지합니다.
        private readonly MetricCollector _collector;
        private readonly AnalysisEngine _engine;
        private readonly UnityBridge _unity;
        private readonly TrayIconManager _tray;

        // DispatcherTimer: WPF에서 UI 스레드에서 주기적으로 코드를 실행하는 타이머입니다.
        // UI 스레드에서 실행되므로 UI 컴포넌트에 안전하게 접근할 수 있습니다.
        // '?'는 nullable을 의미합니다 — 이 변수는 null일 수도 있다는 뜻입니다.
        private DispatcherTimer? _timer;

        private int _tickCounter;

        // List<int>: 크기가 동적으로 변하는 정수 배열입니다.
        // 최근 10분의 집중도/상태 기록을 저장합니다.
        private readonly List<int> _historyScores = new();
        private readonly List<int> _historyStates = new();

        // 매 틱마다 갱신되는 최신 스냅샷을 캐시합니다.
        // DashboardWindow의 ISessionData 접근 시 매번 GetSnapshot()을 호출하지 않아도 됩니다.
        private MetricSnapshot _lastSnapshot = new(0, 0, 0, 0, 0, 0, 0);

        // 생성자(Constructor): 클래스가 new로 생성될 때 실행됩니다.
        // 필요한 의존 객체들을 외부에서 주입받습니다 (의존성 주입 패턴).
        // 이렇게 하면 각 서비스가 독립적으로 테스트 가능하고, 결합도가 낮아집니다.
        public MonitoringService(MetricCollector collector, AnalysisEngine engine, UnityBridge unity, TrayIconManager tray)
        {
            _collector = collector;
            _engine = engine;
            _unity = unity;
            _tray = tray;
        }

        // 타이머를 시작합니다. App.xaml.cs의 OnStartup에서 호출됩니다.
        public void Start()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

            // '+= OnTick': 타이머가 1초마다 OnTick 메서드를 호출하도록 등록합니다.
            _timer.Tick += OnTick;
            _timer.Start();
        }

        // 타이머를 중단합니다. App 종료 시 호출됩니다.
        public void Stop() => _timer?.Stop();

        // 사용자가 '데이터 초기화'를 선택했을 때 호출됩니다.
        // 타이머 카운터, 기록, 수집 데이터, 엔진 베이스라인을 모두 초기화합니다.
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
        // object? sender: 이벤트를 발생시킨 타이머 객체
        // EventArgs e: 추가 이벤트 정보 (타이머는 별도 정보 없음)
        private void OnTick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            _tickCounter++;

            // 1. MetricCollector에게 이번 틱 처리를 지시합니다 (오래된 데이터 정리 + 창 전환 감지).
            _collector.Tick(now);

            // 2. 현재 지표 스냅샷을 가져와 캐시합니다.
            _lastSnapshot = _collector.GetSnapshot();

            // 3. AnalysisEngine의 실시간 상태(Idle/Distracted)를 갱신합니다.
            _engine.UpdateRealtimeStatus(
                _lastSnapshot.Kpm,
                _lastSnapshot.Mpm,
                _lastSnapshot.ContextSwitchCount,
                _engine.IsFirstAnalysisComplete
            );

            // 4. 60초마다 심층 분석을 실행합니다.
            if (_tickCounter >= 60)
            {
                // TotalAccumulatedKeys를 엔진과 동기화합니다.
                _engine.TotalAccumulatedKeys = _collector.TotalAccumulatedKeys;

                // 이번 분기 DwellTime/FlightTime 평균이 없으면 엔진의 개인 베이스라인으로 대체합니다.
                double avgDt = _lastSnapshot.AvgDwellTime > 0 ? _lastSnapshot.AvgDwellTime : _engine.PersonalEmaDt;
                double avgFt = _lastSnapshot.AvgFlightTime > 0 ? _lastSnapshot.AvgFlightTime : _engine.PersonalEmaFt;

                _engine.PerformDeepAnalysis(
                    _lastSnapshot.Kpm, _lastSnapshot.Mpm,
                    _lastSnapshot.BackspaceCount, _lastSnapshot.JerkCount,
                    _lastSnapshot.ContextSwitchCount, avgDt, avgFt
                );

                UpdateHistory();

                // 다음 분기를 위해 타이밍 누적값을 초기화합니다.
                _collector.ResetTimingAccumulators();
                _tickCounter = 0;
            }

            // 5. 트레이 툴팁을 최신 상태로 갱신합니다.
            UpdateTrayTooltip();

            // 6. Unity 캐릭터 애니메이션 상태를 전송합니다.
            _unity.SendState(_engine.FocusState);
        }

        // 매 60초마다 현재 집중도/상태를 기록에 추가합니다.
        private void UpdateHistory()
        {
            _historyScores.Add(_engine.FocusScore);
            _historyStates.Add(_engine.FocusState);

            // 최대 10분치 기록만 유지합니다. 오래된 항목은 맨 앞에서 제거합니다.
            if (_historyScores.Count > 10)
            {
                _historyScores.RemoveAt(0);
                _historyStates.RemoveAt(0);
            }
        }

        // 트레이 아이콘 툴팁 텍스트를 현재 상태에 맞게 갱신합니다.
        private void UpdateTrayTooltip()
        {
            if (!_engine.IsFirstAnalysisComplete)
            {
                _tray.UpdateTooltip($"⏳ 패턴 분석 중... ({60 - _tickCounter}초)");
                return;
            }

            string[] stateNames = { "Idle ☕", "Distracted 😵‍💫", "Engaged 🙂", "Focused 🤓", "Deep Focus 🔥" };

            // Math.Clamp: 값이 min~max 범위를 벗어나지 않도록 제한합니다.
            string stateText = stateNames[Math.Clamp(_engine.FocusState, 0, 4)];
            _tray.UpdateTooltip($"🎯 {stateText} ({_engine.FocusScore}%)\nKPM: {_lastSnapshot.Kpm} | 창 전환: {_lastSnapshot.ContextSwitchCount}회");
        }

        // ── ISessionData 구현부 ─────────────────────────────────────────────────
        // 아래 속성들은 DashboardWindow가 ISessionData를 통해 접근하는 데이터입니다.
        // '=>'는 표현식 본문(Expression-bodied member)으로, 간결하게 get을 정의합니다.
        // 예: public bool IsFirstAnalysisComplete { get { return _engine.IsFirstAnalysisComplete; } } 와 동일.

        public bool IsFirstAnalysisComplete => _engine.IsFirstAnalysisComplete;
        public int RemainingSeconds => 60 - _tickCounter;
        public int FocusScore => _engine.FocusScore;
        public int StressScore => _engine.StressScore;

        // 아래 4개는 _lastSnapshot(캐시된 스냅샷)에서 읽습니다.
        // 매 호출마다 GetSnapshot()을 재계산하지 않아 효율적입니다.
        public int CurrentKpm => _lastSnapshot.Kpm;
        public int CurrentMpm => _lastSnapshot.Mpm;
        public int CurrentApm => _lastSnapshot.Kpm + _lastSnapshot.Mpm;
        public int BackspaceCount => _lastSnapshot.BackspaceCount;
        public int JerkCount => _lastSnapshot.JerkCount;
        public int ContextSwitchCount => _lastSnapshot.ContextSwitchCount;

        public int FocusState => _engine.FocusState;
        public string StateReason => _engine.StateReason;

        // new List<int>(_historyScores): 원본 리스트의 복사본을 반환합니다.
        // 호출자가 반환된 리스트를 수정해도 내부 _historyScores에는 영향 없습니다.
        public List<int> HistoryScores => new List<int>(_historyScores);
    }
}
