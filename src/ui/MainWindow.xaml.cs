using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Keymon
{
    public partial class MainWindow : Window
    {
        private readonly ISessionData _session;
        private DispatcherTimer? _animTimer;

        private readonly Dictionary<int, List<BitmapImage>> _animationGroups = new();
        private int _currentAnimationNum = 1;
        private int _targetAnimationNum = 1;
        private int _currentFrameIdx = 0;
        private int _transitionDelayCounter = 0;

        public MainWindow(ISessionData session)
        {
            InitializeComponent();
            _session = session;

            LoadAllAnimationFrames();

            _animTimer = new DispatcherTimer(DispatcherPriority.Render);
            _animTimer.Interval = TimeSpan.FromMilliseconds(120);
            _animTimer.Tick += OnAnimationTick;
            _animTimer.Start();
        }

        private void LoadAllAnimationFrames()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string baseDir = Path.GetFullPath(Path.Combine(exeDir, @"..\..\..\"));

            for (int animNum = 1; animNum <= 5; animNum++)
            {
                var frames = new List<BitmapImage>();

                for (int i = 1; i <= 12; i++)
                {
                    string path = Path.Combine(baseDir, "Assets", $"Anim{animNum}", $"{i}.png");

                    if (File.Exists(path))
                    {
                        try
                        {
                            BitmapImage bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(path, UriKind.Absolute);
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            frames.Add(bitmap);
                        }
                        catch { }
                    }
                }
                if (frames.Count > 0) _animationGroups[animNum] = frames;
            }
        }

        private void OnAnimationTick(object? sender, EventArgs e)
        {
            if (_session.IsStandby)
            {
                if (_animationGroups.ContainsKey(1) && _animationGroups[1].Count > 0)
                {
                    AnimImage.Source = _animationGroups[1][0];
                }
                _currentAnimationNum = 1;
                _targetAnimationNum = 1;
                _currentFrameIdx = 0;
                _transitionDelayCounter = 0;
                return;
            }

            UpdateAnimationByState(_session.FocusState);

            if (!_animationGroups.ContainsKey(_currentAnimationNum)) return;
            var currentSet = _animationGroups[_currentAnimationNum];

            if (_currentFrameIdx < currentSet.Count)
            {
                AnimImage.Source = currentSet[_currentFrameIdx];
            }

            if (_currentFrameIdx == 0)
            {
                _transitionDelayCounter++;
                if (_transitionDelayCounter < 24)
                {
                    return;
                }
                _transitionDelayCounter = 0;
            }

            _currentFrameIdx++;

            if (_currentFrameIdx >= currentSet.Count)
            {
                if (_currentAnimationNum != _targetAnimationNum)
                {
                    _currentAnimationNum = _targetAnimationNum;
                    _currentFrameIdx = 0;
                    _transitionDelayCounter = 24;
                }
                else
                {
                    _currentFrameIdx = 1;
                }
            }
        }

        private void UpdateAnimationByState(int focusState)
        {
            int nextAnimNum = focusState switch
            {
                0 => 1,
                1 => 2,
                2 => 3,
                3 => 4,
                4 => 5,
                _ => 1
            };

            _targetAnimationNum = nextAnimNum;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var workingArea = SystemParameters.WorkArea;
            this.Left = workingArea.Right - this.Width - 20;
            this.Top = workingArea.Bottom - this.Height - 20;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }
    }
}