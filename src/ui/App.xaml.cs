using System;
using System.Windows;

// App.xaml.cs의 역할 (리팩토링 후):
//   오직 '조합(Composition)'만 담당합니다.
//   모든 서비스를 생성하고, 의존성을 연결하고, 시작/종료 신호를 보내는 것이 전부입니다.
//   비즈니스 로직, 데이터 수집, 분석 — 이 중 어느 것도 여기에 없습니다.
//
// 이전 App.xaml.cs: ~400줄, 큰 큐 6개, 입력 핸들러 4개, 분석 로직, Unity 관리, 파일 I/O 등
// 현재 App.xaml.cs: ~45줄, 서비스 생성 및 연결만

namespace Keymon
{
    // 'partial' 키워드: 이 클래스의 나머지 코드가 App.xaml(자동생성)에 있음을 의미합니다.
    // WPF에서 XAML 파일과 .cs 파일은 partial class로 하나의 클래스를 나눠 정의합니다.
    public partial class App : Application
    {
        // '?'(nullable): Start() 전까지는 null이므로 nullable로 선언합니다.
        private InputHookManager? _hookManager;
        private MetricCollector? _collector;
        private AnalysisEngine? _engine;
        private MonitoringService? _monitoring;
        private UnityBridge? _unity;
        private TrayIconManager? _tray;
        private PersistenceService? _persistence;

        // DashboardWindow는 처음에는 null이고, 사용자가 열 때 한 번만 생성됩니다.
        private DashboardWindow? _dashboardWindow;

        // 앱이 시작될 때 WPF가 자동으로 호출합니다.
        // 'override'는 부모 클래스(Application)의 메서드를 재정의한다는 의미입니다.
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 트레이 앱이므로 모든 창이 닫혀도 앱이 종료되지 않도록 설정합니다.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // 1. 모든 서비스를 생성합니다.
            _engine = new AnalysisEngine();
            _hookManager = new InputHookManager();
            _collector = new MetricCollector();
            _persistence = new PersistenceService();
            _unity = new UnityBridge();
            _tray = new TrayIconManager();

            // MonitoringService는 다른 서비스들을 필요로 하므로 마지막에 생성합니다.
            _monitoring = new MonitoringService(_collector, _engine, _unity, _tray);

            // 2. 이전 세션 데이터를 로드합니다.
            _persistence.Load(_engine, _collector);

            // 3. MetricCollector가 InputHookManager의 이벤트를 구독하게 합니다.
            _collector.Subscribe(_hookManager);

            // 4. 트레이 메뉴 액션을 연결합니다.
            // '() =>' 람다(lambda): 이름 없는 짧은 함수를 정의하는 문법입니다.
            _tray.OnShowDashboard = ShowDashboard;
            _tray.OnResetData = () => _monitoring.Reset();
            _tray.OnExit = () => Current.Shutdown();
            _tray.Initialize();

            // 5. 각 서비스를 순서대로 시작합니다.
            _hookManager.Start();
            _unity.Start();
            _monitoring.Start();
        }

        // 트레이 메뉴의 '대시보드' 또는 아이콘 더블클릭 시 호출됩니다.
        private void ShowDashboard()
        {
            // null coalescing assignment (??=): _dashboardWindow가 null일 때만 새로 생성합니다.
            // 이미 생성된 경우 기존 인스턴스를 재사용합니다 (중복 창 방지).
            _dashboardWindow ??= new DashboardWindow(_monitoring!);
            _dashboardWindow.Show();
            _dashboardWindow.Activate();
        }

        // 앱이 종료될 때 WPF가 자동으로 호출합니다.
        protected override void OnExit(ExitEventArgs e)
        {
            // '?.'(null-conditional): 객체가 null이 아닐 때만 메서드를 호출합니다.
            // 초기화 중 오류가 발생해 일부 서비스가 null인 경우를 안전하게 처리합니다.
            _monitoring?.Stop();
            _hookManager?.Stop();
            _tray?.Dispose();
            _unity?.Dispose();

            // '!'(null-forgiving): 여기까지 도달했다면 반드시 초기화된 상태이므로 null이 아님을 단언합니다.
            _persistence?.Save(_engine!, _collector!);

            base.OnExit(e);
            Environment.Exit(0);
        }
    }
}
