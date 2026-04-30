using System;
using System.Windows;

// App.xaml.cs의 역할 (리팩토링 후):
//   오직 '조합(Composition)'만 담당합니다.
//   모든 서비스를 생성하고, 의존성을 연결하고, 시작/종료 신호를 보내는 것이 전부입니다.

namespace Keymon
{
    public partial class App : Application
    {
        private InputHookManager? _hookManager;
        private MetricCollector? _collector;
        private AnalysisEngine? _engine;
        private MonitoringService? _monitoring;
        private UnityBridge? _unity;
        private TrayIconManager? _tray;
        private PersistenceService? _persistence;

        private DashboardWindow? _dashboardWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // 1. 모든 서비스를 생성합니다.
            _engine = new AnalysisEngine();
            _hookManager = new InputHookManager();
            _collector = new MetricCollector();
            _persistence = new PersistenceService();
            _unity = new UnityBridge();
            _tray = new TrayIconManager();
            _monitoring = new MonitoringService(_collector, _engine, _unity, _tray);

            // 2. 이전 세션 데이터를 로드합니다.
            _persistence.Load(_engine, _collector);

            // 3. MetricCollector가 InputHookManager의 이벤트를 구독하게 합니다.
            _collector.Subscribe(_hookManager);

            // 4. 트레이 메뉴 액션을 연결합니다.
            _tray.OnShowDashboard = ShowDashboard;
            _tray.OnResetData = () => _monitoring.Reset();
            _tray.OnExit = () => Current.Shutdown();
            _tray.Initialize();

            // 5. 각 서비스를 순서대로 시작합니다.
            _hookManager.Start();
            _unity.Start();
            _monitoring.Start();
        }

        private void ShowDashboard()
        {
            _dashboardWindow ??= new DashboardWindow(_monitoring!);
            _dashboardWindow.Show();
            _dashboardWindow.Activate();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _monitoring?.Stop();
            _hookManager?.Stop();
            _tray?.Dispose();
            _unity?.Dispose();
            _persistence?.Save(_engine!, _collector!);
            base.OnExit(e);
            Environment.Exit(0);
        }
    }
}
