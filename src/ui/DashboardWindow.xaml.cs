using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Keymon
{
    public partial class DashboardWindow : Window
    {
        // ISessionData: 구체적인 클래스(MonitoringService) 대신 인터페이스를 참조합니다.
        // 이 덕분에 DashboardWindow는 데이터가 어디서 오는지 알 필요 없이,
        // "이 인터페이스를 구현한 누군가"로부터 데이터를 읽기만 합니다.
        private readonly ISessionData _session;

        private DispatcherTimer _uiTimer;
        private string _currentAnimState = "";

        // 생성자: App.xaml.cs의 ShowDashboard()에서 MonitoringService를 ISessionData로 전달합니다.
        // App → new DashboardWindow(monitoringService) → _session = monitoringService
        public DashboardWindow(ISessionData session)
        {
            InitializeComponent();
            _session = session;

            // 1초마다 UpdateDashboard를 호출하는 타이머를 시작합니다.
            _uiTimer = new DispatcherTimer();
            _uiTimer.Interval = TimeSpan.FromSeconds(1);
            _uiTimer.Tick += UpdateDashboard;
            _uiTimer.Start();
        }

        // 1초마다 호출됩니다. ISessionData에서 최신값을 읽어 UI를 갱신합니다.
        private void UpdateDashboard(object? sender, EventArgs e)
        {
            // '_session.현재속성'으로 데이터를 읽습니다. (이전: _app.GetCurrentKPM())
            TxtKpm.Text = _session.CurrentKpm.ToString();
            TxtMpm.Text = _session.CurrentMpm.ToString();
            TxtApm.Text = _session.CurrentApm.ToString();
            TxtFocus.Text = $"{_session.FocusScore}%";

            TxtEr.Text = $"{_session.BackspaceCount} 회";
            TxtCsr.Text = $"{_session.ContextSwitchCount} 번";
            TxtJerk.Text = $"{_session.JerkCount} 회";

            // 1. 뇌 과부하 대신 피로도 텍스트 업데이트
            TxtFatigue.Text = $"{(int)_session.FatigueScore} 점";

            // 2. 피로도 수치에 따라 직관적으로 글자색 변경
            if (_session.FatigueScore >= 71)
            {
                TxtFatigue.Foreground = new SolidColorBrush(Colors.IndianRed); // 위험: 붉은색 (매우 지침)
            }
            else if (_session.FatigueScore >= 31)
            {
                TxtFatigue.Foreground = new SolidColorBrush(Colors.DarkOrange); // 주의: 주황색 (슬슬 피곤함)
            }
            else
            {
                TxtFatigue.Foreground = new SolidColorBrush(Colors.MediumSeaGreen); // 안전: 초록색 (컨디션 좋음)
            }

            TxtReason.Text = _session.StateReason;

            DrawHistoryChart();

            int remaining = _session.RemainingSeconds;
            TxtUpdateSec.Text = $"{remaining}초";
            BarUpdate.Value = remaining;

            // 첫 60초 분석 전: 대기 애니메이션 표시
            if (!_session.IsFirstAnalysisComplete && !_session.IsStandby)
            {
                TxtStatus.Text = "나의 집중 패턴 모니터링";
                TxtCharacter.Text = "⏳";
                TxtStateTitle.Text = "패턴 분석 중";
                TxtCharState.Text = "정밀한 기준을 세우는 중입니다.";
                CharGlow.Color = Colors.LightBlue;

                if (_currentAnimState != "AnimIdle")
                {
                    if (!string.IsNullOrEmpty(_currentAnimState))
                        ((Storyboard)FindResource(_currentAnimState)).Stop();
                    ((Storyboard)FindResource("AnimIdle")).Begin();
                    _currentAnimState = "AnimIdle";
                }
                return;
            }

            // 분석 완료 후: 집중 상태에 맞는 캐릭터 애니메이션 표시
            TxtStatus.Text = "나의 집중 패턴 모니터링";
            UpdateCharacterAnimation(_session.FocusState);
        }

        private void UpdateCharacterAnimation(int focusState)
        {
            string targetState = "";
            string emoji = "";
            string title = "";
            string desc = "";
            Color glowColor = Colors.LightGray;

            if (_session.IsStandby)
            {
                targetState = "AnimIdle";
                emoji = "💤";
                title = "Standby (수집 일시정지)";
                desc = "비입력 감지로 데이터 오염을 방지하고 있습니다.";
                glowColor = Colors.DarkGray;
            }
            else
            {
                // switch 문: focusState 값에 따라 다른 케이스를 실행합니다.
                switch (focusState)
                {
                    case 4:
                        targetState = "AnimDeepFocus"; emoji = "🔥"; title = "Deep Focus";
                        desc = "최상의 효율입니다! 이대로 쭉 가보세요."; glowColor = Colors.OrangeRed; break;
                    case 3:
                        targetState = "AnimFocused"; emoji = "🤓"; title = "Focused";
                        desc = "안정적인 집중 상태입니다."; glowColor = Colors.DodgerBlue; break;
                    case 2:
                        targetState = "AnimEngaged"; emoji = "🙂"; title = "Engaged";
                        desc = "보통 수준의 활동을 유지하고 있습니다."; glowColor = Colors.LightGreen; break;
                    case 1:
                        targetState = "AnimDistracted"; emoji = "😵‍💫"; title = "Distracted";
                        desc = "주의가 분산되었습니다! 업무 효율이 급감 중입니다."; glowColor = Colors.Gold; break;
                    default:
                        targetState = "AnimIdle"; emoji = "⚠️"; title = "IDLE (정지됨)";
                        desc = "APM 15 미만으로 업무 효율 낮음 - 지속적인 도움 필요 🆘"; glowColor = Colors.IndianRed; break;
                }
            }

            TxtCharacter.Text = emoji;
            TxtStateTitle.Text = title;
            TxtCharState.Text = desc;
            CharGlow.Color = glowColor;

            // 상태가 실제로 바뀔 때만 애니메이션을 전환합니다 (불필요한 재시작 방지).
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

        // X 버튼을 눌러도 창을 닫지 않고 숨깁니다 (백그라운드 앱이므로).
        private void DrawHistoryChart()
        {
            var scores = _session.HistoryScores;
            var fatigue = _session.HistoryFatigue;

            ChartArea.Children.Clear();

            for (int i = 0; i < scores.Count; i++)
            {
                // --- 1. 집중도 바 세트 ---
                StackPanel focusGroup = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };

                // 집중도 수치 텍스트
                TextBlock focusVal = new TextBlock
                {
                    Text = $"{scores[i]}",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 2)
                };

                Border focusBar = new Border
                {
                    Width = 16,
                    Height = Math.Max(scores[i] * 1.5, 4), // 비율 살짝 조정
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")),
                    CornerRadius = new CornerRadius(3, 3, 0, 0)
                };
                focusGroup.Children.Add(focusVal);
                focusGroup.Children.Add(focusBar);

                // --- 2. 피로도 바 세트 ---
                StackPanel fatigueGroup = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 0, 0) };

                // 피로도 수치 텍스트
                TextBlock fatigueVal = new TextBlock
                {
                    Text = $"{fatigue[i]}",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 2)
                };

                Border fatigueBar = new Border
                {
                    Width = 16,
                    Height = Math.Max(fatigue[i] * 1.5, 4),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
                    CornerRadius = new CornerRadius(3, 3, 0, 0)
                };
                fatigueGroup.Children.Add(fatigueVal);
                fatigueGroup.Children.Add(fatigueBar);

                // --- 3. 하단 레이아웃 구성 ---
                StackPanel barPair = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
                barPair.Children.Add(focusGroup);
                barPair.Children.Add(fatigueGroup);

                int minsAgo = scores.Count - 1 - i;
                TextBlock timeLabel = new TextBlock
                {
                    Text = minsAgo == 0 ? "방금" : $"-{minsAgo}분",
                    FontSize = 10,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 5, 0, 0)
                };

                StackPanel column = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Bottom };
                column.Children.Add(barPair);
                column.Children.Add(timeLabel);

                ChartArea.Children.Add(column);
            }
        }
    }
}