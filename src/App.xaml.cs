using System;
using System.Windows;
using System.Threading.Tasks;

namespace Keymon
{
    public partial class App : Application
    {
        private InputHookManager? _hookManager;
        private MetricCollector? _collector;
        private AnalysisEngine? _engine;
        private MonitoringService? _monitoring;
        private TrayIconManager? _tray;
        private PersistenceService? _persistence;

        private DashboardWindow? _dashboardWindow;
        private MainWindow? _overlayWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                if (args.Exception.InnerException is ObjectDisposedException)
                {
                    args.SetObserved();
                }
            };

            _engine = new AnalysisEngine();
            _hookManager = new InputHookManager();
            _collector = new MetricCollector();
            _persistence = new PersistenceService();
            _tray = new TrayIconManager();

            // 💡 유니티 매개변수 제거됨
            _monitoring = new MonitoringService(_collector, _engine, _tray, _persistence);

            _collector.Subscribe(_hookManager);

            _tray.OnShowDashboard = ShowDashboard;
            _tray.OnResetData = () => _monitoring.Reset();
            _tray.OnExit = () => Current.Shutdown();

            _tray.OnToggleOverlay = (isVisible) =>
            {
                if (isVisible) _overlayWindow?.Show();
                else _overlayWindow?.Hide();
            };

            _tray.OnToggleManualStandby = () =>
            {
                _monitoring.ToggleManualStandby();
                _tray.SyncManualStandbyState(_monitoring.IsManualStandby);
            };

            _tray.Initialize();

            _hookManager.Start();
            _monitoring.Start();

            _overlayWindow = new MainWindow(_monitoring!);
            _overlayWindow.Show();

            ShowDashboard();
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

            _overlayWindow?.Close();
            _dashboardWindow?.Close();

            base.OnExit(e);
            Environment.Exit(0);
        }
    }
}