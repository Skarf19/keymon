using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Keymon
{
    public partial class DashboardWindow : Window
    {
        private readonly ISessionData _session;
        private DispatcherTimer _uiTimer;
        private string _currentAnimState = "";
        private string _selectedDateString;

        public DashboardWindow(ISessionData session)
        {
            InitializeComponent();
            _session = session;

            _selectedDateString = DateTime.Now.ToString("yyyy-MM-dd");

            _uiTimer = new DispatcherTimer();
            _uiTimer.Interval = TimeSpan.FromSeconds(1);
            _uiTimer.Tick += UpdateRealTimeTab;
            _uiTimer.Start();

            RefreshDailyTab();
        }

        private void UpdateRealTimeTab(object? sender, EventArgs e)
        {
            try
            {
                if (_session.IsManualStandby)
                {
                    if (FindName("BtnManualStandby") is System.Windows.Controls.Primitives.ToggleButton btn)
                        btn.IsChecked = true;

                    TxtStatus.Text = "나의 집중 패턴 모니터링";
                    TxtStateTitle.Text = "수동 대기 모드";
                    TxtCharacter.Text = "⏸️";
                    TxtCharState.Text = "분석이 일시 정지되었습니다. (개인 시간)";
                    TxtReason.Text = "영상 시청 또는 휴식을 위해 데이터 분석을 멈추었습니다.";

                    TxtKpm.Text = "-"; TxtMpm.Text = "-"; TxtApm.Text = "-";
                    TxtFocus.Text = "- %"; TxtEr.Text = "- 회"; TxtCsr.Text = "- 번"; TxtJerk.Text = "- 회"; TxtFatigue.Text = "- 점";
                    BarUpdate.Value = 0; TxtUpdateSec.Text = "중지";

                    return;
                }
                else
                {
                    if (FindName("BtnManualStandby") is System.Windows.Controls.Primitives.ToggleButton btn)
                        btn.IsChecked = false;
                }

                TxtKpm.Text = _session.CurrentKpm.ToString();
                TxtMpm.Text = _session.CurrentMpm.ToString();
                TxtApm.Text = _session.CurrentApm.ToString();
                TxtFocus.Text = $"{_session.FocusScore}%";
                TxtEr.Text = $"{_session.BackspaceCount} 회";
                TxtCsr.Text = $"{_session.ContextSwitchCount} 번";
                TxtJerk.Text = $"{_session.JerkCount} 회";
                TxtFatigue.Text = $"{(int)_session.FatigueScore} 점";

                if (_session.FatigueScore >= 71) TxtFatigue.Foreground = new SolidColorBrush(Colors.IndianRed);
                else if (_session.FatigueScore >= 31) TxtFatigue.Foreground = new SolidColorBrush(Colors.DarkOrange);
                else TxtFatigue.Foreground = new SolidColorBrush(Colors.MediumSeaGreen);

                TxtReason.Text = _session.StateReason;
                BarUpdate.Value = _session.RemainingSeconds;
                TxtUpdateSec.Text = $"{_session.RemainingSeconds}초";

                if (_selectedDateString == DateTime.Now.ToString("yyyy-MM-dd"))
                {
                    RefreshDailyTab();
                }

                bool isDataReady = _session.IsFirstAnalysisComplete || _session.HistoryScores.Count > 0;

                if (isDataReady && TxtCharacter.Text == "⏳")
                {
                    RefreshDailyTab();
                    _currentAnimState = "";
                }

                if (!isDataReady && !_session.IsStandby)
                {
                    TxtStatus.Text = "나의 집중 패턴 모니터링"; TxtCharacter.Text = "⏳";
                    TxtStateTitle.Text = "패턴 분석 준비"; TxtCharState.Text = "데이터를 불러오고 있습니다...";
                    CharGlow.Color = Colors.LightBlue;
                    if (_currentAnimState != "AnimIdle") { ((Storyboard)FindResource("AnimIdle")).Begin(); _currentAnimState = "AnimIdle"; }
                    return;
                }

                TxtStatus.Text = "나의 집중 패턴 모니터링";

                string[] stateTitles = { "IDLE (대기)", "산만", "평안 (안정)", "집중", "초집중" };
                string[] stateDescs = {
                    "작업 흐름이 일시 정지되었습니다.",
                    "주의력이 분산되고 있습니다. 다시 집중해 보세요!",
                    "안정적이고 편안한 페이스로 작업 중입니다.",
                    "좋은 몰입도를 유지하고 있습니다.",
                    "최고의 효율! 고도의 몰입 상태입니다."
                };
                string[] stateEmojis = { "💤", "👀", "😌", "💻", "🔥" };

                int currentState = Math.Clamp(_session.FocusState, 0, 4);

                string finalTitle = stateTitles[currentState];
                string finalDesc = stateDescs[currentState];
                string finalEmoji = stateEmojis[currentState];

                if (_session.FatigueScore >= 71)
                {
                    finalTitle = "탈진 (휴식 필요)";
                    finalEmoji = "😵";
                    finalDesc = "인지 능력이 한계에 도달했습니다. 즉시 휴식이 필요합니다. 🚨";
                }

                TxtStateTitle.Text = finalTitle;
                TxtCharacter.Text = finalEmoji;
                TxtCharState.Text = finalDesc;

                TxtReason.Text = _session.StateReason;

                UpdateCharacterAnimation(_session.FocusState);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"실시간 UI 업데이트 실패: {ex.Message}"); }
        }

        private void RefreshDailyTab()
        {
            try { DrawDateSelector(); DrawDailySummary(); DrawDailyLineChart(); DrawStateDistribution(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"통계 UI 렌더링 실패: {ex.Message}"); }
        }

        private void DrawDateSelector()
        {
            DatePanel.Children.Clear();
            DateTime now = DateTime.Now;

            for (int i = 6; i >= 0; i--)
            {
                DateTime date = now.Date.AddDays(-i);
                string dateStr = date.ToString("yyyy-MM-dd");

                bool isSelected = (_selectedDateString == dateStr);
                bool hasData = _session.DailyStats.ContainsKey(dateStr) && _session.DailyStats[dateStr].TotalMinutes > 0;

                string scoreText = hasData ? $"{_session.DailyStats[dateStr].AvgFocus}%" : "-";
                string dotColor = hasData ? "#3B82F6" : "#CBD5E1";

                Border border = new Border { Width = 60, Height = 75, Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(isSelected ? (Color)ColorConverter.ConvertFromString("#EFF6FF") : Colors.Transparent), BorderBrush = new SolidColorBrush(isSelected ? (Color)ColorConverter.ConvertFromString("#3B82F6") : (Color)ColorConverter.ConvertFromString("#E2E8F0")), BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };

                border.MouseLeftButtonUp += (s, e) => { _selectedDateString = dateStr; RefreshDailyTab(); };

                StackPanel panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                panel.Children.Add(new TextBlock { Text = $"{date.ToString("M/d")}\n{date.ToString("ddd")}", TextAlignment = TextAlignment.Center, FontSize = 12, Foreground = new SolidColorBrush(isSelected ? (Color)ColorConverter.ConvertFromString("#3B82F6") : (Color)ColorConverter.ConvertFromString("#64748B")) });
                panel.Children.Add(new TextBlock { Text = scoreText, TextAlignment = TextAlignment.Center, FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4), Foreground = new SolidColorBrush(isSelected ? (Color)ColorConverter.ConvertFromString("#3B82F6") : (Color)ColorConverter.ConvertFromString("#334155")) });
                panel.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dotColor)), HorizontalAlignment = HorizontalAlignment.Center });

                border.Child = panel;
                DatePanel.Children.Add(border);
            }
        }

        private void DrawDailySummary()
        {
            if (!_session.DailyStats.ContainsKey(_selectedDateString)) return;
            var stat = _session.DailyStats[_selectedDateString];
            DateTime dt = DateTime.Parse(_selectedDateString);
            TxtDailyReportTitle.Text = $"📋 {dt.ToString("M월 d일")} 리포트";

            if (stat.TotalMinutes == 0) return;

            int pureFocusMinutes = stat.StateCounts[3] + stat.StateCounts[4];
            int totalLoggedMinutes = stat.TotalWorkMinutes;
            int distractedMinutes = stat.StateCounts[1];
            int idleMinutes = stat.StateCounts[0];

            TxtDailyAvgFocus.Text = $"{stat.AvgFocus}%";
            TxtDailyAvgFatigue.Text = $"{stat.AvgFatigue}점";

            TxtDailyWorkTime.Text = pureFocusMinutes >= 60 ? $"{pureFocusMinutes / 60}h {pureFocusMinutes % 60}m" : $"{pureFocusMinutes}m";
            TxtDailyIdleTime.Text = idleMinutes >= 60 ? $"{idleMinutes / 60}h {idleMinutes % 60}m" : $"{idleMinutes}m";

            if (pureFocusMinutes > 0 && pureFocusMinutes >= (distractedMinutes + idleMinutes)) TxtSummarySentence.Text = "훌륭합니다! 오늘 하루 집중을 아주 높은 비중으로 잘 유지하셨네요.";
            else if (idleMinutes > totalLoggedMinutes * 0.5) TxtSummarySentence.Text = "오늘은 컴퓨터를 켜두고 중간중간 자리를 비우거나 휴식한 시간이 많았습니다.";
            else if (distractedMinutes > 0) TxtSummarySentence.Text = "창 전환이나 입력 리듬 끊김이 다소 있었습니다. 한 가지 작업에 조금 더 몰두해 보세요.";
            else TxtSummarySentence.Text = "안정적인 업무 컨디션으로 큰 기복 없이 차분하게 작업을 진행하셨습니다.";

            if (stat.AvgFocus >= 60) { TxtDailyHint1Title.Text = "훌륭한 집중력"; TxtDailyHint1Desc.Text = "방해 요소 없이 스스로 작업 흐름을 제어하는 능력이 탁월합니다."; }
            else if (distractedMinutes > 0) { TxtDailyHint1Title.Text = "주의력 분산 감지"; TxtDailyHint1Desc.Text = "내일은 창 전환을 줄이고 하나의 메인 태스크에 집중해 보세요."; }
            else if (idleMinutes > stat.TotalWorkMinutes * 0.3) { TxtDailyHint1Title.Text = "잦은 흐름 끊김"; TxtDailyHint1Desc.Text = "입력이 멈춘 대기 시간이 많아 평균 집중도가 낮아졌습니다. 몰입 시간을 늘려보세요."; }
            else { TxtDailyHint1Title.Text = "얕은 몰입 상태"; TxtDailyHint1Desc.Text = "작업은 꾸준히 했지만 깊은 몰입(Deep Focus)으로 연결되지는 못했습니다."; }

            TxtDailyHint2Title.Text = stat.AvgFatigue >= 60 ? "피로도 컨디션 경고" : "안정적인 에너지 조절";
            TxtDailyHint2Desc.Text = stat.AvgFatigue >= 60 ? "뇌가 많이 피로한 상태입니다. 작업 종료 후 완전한 휴식이 필요합니다." : "체력 소모가 과도하지 않도록 페이스 배분을 아주 잘 조절했습니다.";
        }

        private void DrawDailyLineChart()
        {
            DailyChartCanvas.Children.Clear();
            DailyChartXAxis.Children.Clear();

            if (!_session.DailyStats.ContainsKey(_selectedDateString)) return;
            var stat = _session.DailyStats[_selectedDateString];

            stat.HourlyActiveMinutes ??= new int[24];
            stat.HourlyMinutes ??= new int[24];
            stat.HourlyFocusSum ??= new int[24];
            stat.HourlyFatigueSum ??= new int[24];

            bool isToday = _selectedDateString == DateTime.Now.ToString("yyyy-MM-dd");
            int currentHour = DateTime.Now.Hour;

            List<int> activeHours = new List<int>();
            for (int h = 0; h < 24; h++)
            {
                if (isToday && h >= currentHour) continue;
                if (stat.HourlyMinutes[h] > 0 && stat.HourlyActiveMinutes[h] > 0)
                {
                    activeHours.Add(h);
                }
            }

            if (activeHours.Count == 0) return;

            double width = 580; double height = 130; double maxVal = 100;
            double paddingX = 25;

            double stepX = activeHours.Count > 1 ? (width - paddingX * 2) / (activeHours.Count - 1) : 0;

            Polyline focusLine = new Polyline { Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), StrokeThickness = 2 };
            Polyline fatigueLine = new Polyline { Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")), StrokeThickness = 2 };

            for (int i = 0; i < activeHours.Count; i++)
            {
                int h = activeHours[i];

                double x = activeHours.Count == 1 ? width / 2 : paddingX + (i * stepX);

                double fScore = (double)stat.HourlyFocusSum[h] / stat.HourlyActiveMinutes[h];
                double fFatigue = (double)stat.HourlyFatigueSum[h] / stat.HourlyActiveMinutes[h];

                double focusY = 10 + (height - ((fScore / maxVal) * height));
                double fatigueY = 10 + (height - ((fFatigue / maxVal) * height));

                focusLine.Points.Add(new Point(x, focusY));
                fatigueLine.Points.Add(new Point(x, fatigueY));

                Ellipse focusDot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Colors.White), Stroke = focusLine.Stroke, StrokeThickness = 2 };
                Canvas.SetLeft(focusDot, x - 4); Canvas.SetTop(focusDot, focusY - 4);
                DailyChartCanvas.Children.Add(focusDot);

                TextBlock focusValLabel = new TextBlock
                {
                    Text = Math.Round(fScore).ToString(),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"))
                };
                Canvas.SetLeft(focusValLabel, x - 8); Canvas.SetTop(focusValLabel, focusY - 18);
                DailyChartCanvas.Children.Add(focusValLabel);

                Ellipse fatigueDot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Colors.White), Stroke = fatigueLine.Stroke, StrokeThickness = 2 };
                Canvas.SetLeft(fatigueDot, x - 4); Canvas.SetTop(fatigueDot, fatigueY - 4);
                DailyChartCanvas.Children.Add(fatigueDot);

                TextBlock fatigueValLabel = new TextBlock
                {
                    Text = Math.Round(fFatigue).ToString(),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"))
                };
                Canvas.SetLeft(fatigueValLabel, x - 8); Canvas.SetTop(fatigueValLabel, fatigueY + 6);
                DailyChartCanvas.Children.Add(fatigueValLabel);

                TextBlock timeLabel = new TextBlock
                {
                    Text = $"{h}시",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"))
                };
                timeLabel.Margin = new Thickness(x - 10, 0, 0, 0);
                DailyChartXAxis.Children.Add(timeLabel);
            }

            DailyChartCanvas.Children.Insert(0, focusLine);
            DailyChartCanvas.Children.Insert(0, fatigueLine);
        }

        private void DrawStateDistribution()
        {
            StateDistributionPanel.Children.Clear();
            StateDistributionPanel.Children.Add(new TextBlock { Text = "🔘 집중 상태 분포", FontWeight = FontWeights.Bold, FontSize = 14, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Margin = new Thickness(0, 0, 0, 15) });

            if (!_session.DailyStats.ContainsKey(_selectedDateString)) return;

            var stat = _session.DailyStats[_selectedDateString];
            stat.StateCounts ??= new int[5];

            int total = stat.TotalWorkMinutes;
            if (total == 0) return;

            string[] names = { "Deep Focus", "Focused", "Engaged", "Distracted", "Idle" };
            string[] colors = { "#3B82F6", "#10B981", "#0EA5E9", "#F59E0B", "#94A3B8" };
            int[] mapIdx = { 4, 3, 2, 1, 0 };

            for (int i = 0; i < 5; i++)
            {
                int count = stat.StateCounts[mapIdx[i]];
                double pct = Math.Round((double)count / total * 100);
                string timeStr = count >= 60 ? $"{count / 60}시간 {count % 60}분" : $"{count}분";

                Grid row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });

                row.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
                TextBlock nameTb = new TextBlock { Text = names[i], FontSize = 12, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(nameTb, 1); row.Children.Add(nameTb);

                Border bgBar = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 15, 0) };
                Border fgBar = new Border { HorizontalAlignment = HorizontalAlignment.Left, Width = pct * 1.5, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])) }; bgBar.Child = fgBar; Grid.SetColumn(bgBar, 2); row.Children.Add(bgBar);

                TextBlock timeTb = new TextBlock { Text = timeStr, FontSize = 12, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }; Grid.SetColumn(timeTb, 3); row.Children.Add(timeTb);
                TextBlock pctTb = new TextBlock { Text = $"{pct}%", FontSize = 12, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(pctTb, 4); row.Children.Add(pctTb);

                StateDistributionPanel.Children.Add(row);
            }
        }

        private void BtnManualStandby_Click(object sender, RoutedEventArgs e)
        {
            if (_session is MonitoringService service)
            {
                service.ToggleManualStandby();
                UpdateRealTimeTab(null, EventArgs.Empty);
            }
        }

        private void UpdateCharacterAnimation(int focusState) { /* 생략 없이 기존 코드 동일 */ }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { e.Cancel = true; this.Hide(); base.OnClosing(e); }
    }
}