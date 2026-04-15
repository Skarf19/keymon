using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Project1
{
    public partial class DashboardWindow : Window
    {
        private App _app;
        private DispatcherTimer _uiTimer;
        private string _currentAnimState = "";

        public DashboardWindow(App app)
        {
            InitializeComponent();
            _app = app;

            _uiTimer = new DispatcherTimer();
            _uiTimer.Interval = TimeSpan.FromSeconds(1);
            _uiTimer.Tick += UpdateDashboard;
            _uiTimer.Start();
        }

        private void UpdateDashboard(object? sender, EventArgs e)
        {
            // 하단 실시간 데이터 갱신
            TxtKpm.Text = _app.GetCurrentKPM().ToString();
            TxtMpm.Text = _app.GetCurrentMPM().ToString();
            TxtApm.Text = _app.GetCurrentAPM().ToString();
            TxtFocus.Text = $"{_app.GetFocus()}%";

            TxtEr.Text = $"{_app.GetBackspaceCount()} 회";
            TxtCsr.Text = $"{_app.GetContextSwitchCount()} 번";
            TxtJerk.Text = $"{_app.GetJerkCount()} 회";
            TxtStress.Text = $"{_app.GetStress()} 점";

            TxtReason.Text = _app.GetStateReason();

            DrawHistoryChart();

            // 💡 [핵심 추가] 상단 타이머 UI 갱신 (60초 -> 0초로 줄어듦)
            int remaining = _app.GetRemainingSeconds();
            TxtUpdateSec.Text = $"{remaining}초";
            BarUpdate.Value = remaining;

            // 1. 초기 분석 (첫 60초) 대기 상태
            if (!_app.GetIsFirstAnalysisComplete())
            {
                TxtStatus.Text = "나의 집중 패턴 모니터링";

                TxtCharacter.Text = "⏳";
                TxtStateTitle.Text = "패턴 분석 중";
                TxtCharState.Text = "정밀한 기준을 세우는 중입니다."; // (타이머가 위에 생겼으니 남은 시간 텍스트 제거)
                CharGlow.Color = Colors.LightBlue;

                if (_currentAnimState != "AnimIdle")
                {
                    if (!string.IsNullOrEmpty(_currentAnimState))
                        ((Storyboard)FindResource(_currentAnimState)).Stop();

                    ((Storyboard)FindResource("AnimIdle")).Begin();
                    _currentAnimState = "AnimIdle";
                }

                return; // 여기서 멈춤
            }

            // 2. 분석 완료 후 정상 작동 상태
            TxtStatus.Text = "나의 집중 패턴 모니터링";
            UpdateCharacterAnimation(_app.GetFocusState());
        }

        private void UpdateCharacterAnimation(int focusState)
        {
            string targetState = "";
            string emoji = "";
            string title = "";
            string desc = "";
            Color glowColor = Colors.LightGray;

            switch (focusState)
            {
                case 4: // Deep Focus
                    targetState = "AnimDeepFocus"; emoji = "🔥"; title = "Deep Focus"; desc = "최상의 효율입니다! 이대로 쭉 가보세요."; glowColor = Colors.OrangeRed; break;
                case 3: // Focused
                    targetState = "AnimFocused"; emoji = "🤓"; title = "Focused"; desc = "안정적인 집중 상태입니다."; glowColor = Colors.DodgerBlue; break;
                case 2: // Engaged
                    targetState = "AnimEngaged"; emoji = "🙂"; title = "Engaged"; desc = "보통 수준의 활동을 유지하고 있습니다."; glowColor = Colors.LightGreen; break;
                case 1: // Distracted
                    targetState = "AnimDistracted"; emoji = "😵‍💫"; title = "Distracted"; desc = "주의가 분산되었습니다! 업무 효율이 급감 중입니다."; glowColor = Colors.Gold; break;
                default: // 0: Idle (공백 상태 - 적극 개입 모드)
                    targetState = "AnimIdle"; emoji = "⚠️"; title = "IDLE (정지됨)"; desc = "업무 효율 낮음 - 지속적인 도움 필요 🆘"; glowColor = Colors.IndianRed; break;
            }

            // 💡 핵심 해결: 애니메이션 교체 여부와 상관없이 글씨와 색상은 무조건! 갱신합니다.
            TxtCharacter.Text = emoji;
            TxtStateTitle.Text = title;
            TxtCharState.Text = desc;
            CharGlow.Color = glowColor;

            // 애니메이션은 실제로 다른 상태(예: Idle -> Engaged)로 넘어갈 때만 교체합니다.
            if (_currentAnimState != targetState)
            {
                if (!string.IsNullOrEmpty(_currentAnimState))
                    ((Storyboard)FindResource(_currentAnimState)).Stop();

                CharScale.ScaleX = 1; CharScale.ScaleY = 1;
                CharRotate.Angle = 0;
                CharTranslate.X = 0; CharTranslate.Y = 0;

                ((Storyboard)FindResource(targetState)).Begin();
                _currentAnimState = targetState;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void DrawHistoryChart()
        {
            var scores = _app.GetHistoryScores();
            // 상태(states)는 이제 차트 그릴 때 쓰지 않으므로 가져오지 않아도 됩니다.

            ChartArea.Children.Clear();

            if (scores.Count == 0)
            {
                ChartArea.Children.Add(new TextBlock { Text = "아직 기록된 데이터가 없습니다.", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), VerticalAlignment = VerticalAlignment.Center });
                return;
            }

            for (int i = 0; i < scores.Count; i++)
            {
                int score = scores[i];
                double barHeight = Math.Max(score * 1.5, 10);

                // 💡 [UX 핵심 수정] 무조건 똑같은 파란색(집중의 상징)으로 통일! 
                // 이제 사용자는 복잡한 색상 의미를 생각할 필요 없이, 오직 막대기의 '높이'만 직관적으로 받아들입니다.
                Border bar = new Border
                {
                    Width = 32,
                    Height = barHeight,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), // 테마 컬러인 시원한 파란색
                    CornerRadius = new CornerRadius(5, 5, 0, 0),
                    ToolTip = $"집중도: {score}%"
                };

                int minsAgo = scores.Count - i;
                string timeText = minsAgo == 1 ? "방금" : $"-{minsAgo - 1}분";

                TextBlock timeLabel = new TextBlock
                {
                    Text = timeText,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 0)
                };

                StackPanel container = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Bottom
                };

                container.Children.Add(bar);
                container.Children.Add(timeLabel);

                ChartArea.Children.Add(container);
            }
        }

    }

}