using SharpHook;
using SharpHook.Native;
using System;

namespace Project1
{
    public class InputHookManager : IDisposable
    {
        private TaskPoolGlobalHook? _globalHook;

        // App에서 구독하여 데이터를 처리할 수 있도록 이벤트를 노출합니다.
        public event EventHandler<KeyboardHookEventArgs>? KeyPressed;
        public event EventHandler<KeyboardHookEventArgs>? KeyReleased;
        public event EventHandler<MouseHookEventArgs>? MousePressed;
        public event EventHandler<MouseHookEventArgs>? MouseMoved;

        public void Start()
        {
            // 이미 실행 중이면 중복 실행 방지
            if (_globalHook != null) return;

            _globalHook = new TaskPoolGlobalHook();

            // SharpHook의 이벤트를 이 클래스의 이벤트로 연결(Toss)합니다.
            _globalHook.KeyPressed += (s, e) => KeyPressed?.Invoke(this, e);
            _globalHook.KeyReleased += (s, e) => KeyReleased?.Invoke(this, e);
            _globalHook.MousePressed += (s, e) => MousePressed?.Invoke(this, e);
            _globalHook.MouseMoved += (s, e) => MouseMoved?.Invoke(this, e);

            _globalHook.RunAsync();
        }

        public void Stop()
        {
            _globalHook?.Dispose();
            _globalHook = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}