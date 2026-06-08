using SharpHook;
using SharpHook.Native;
using System;
using System.Threading.Tasks;

namespace Keymon
{
    public class InputHookManager : IDisposable
    {
        private TaskPoolGlobalHook? _globalHook;

        // 💡 중요: 이벤트 인자의 데이터 타입을 명확히 하기 위해 SharpHook.Native.KeyCode를 직접 언급하지 않는 방향으로 안전하게 설계합니다.
        public event EventHandler<KeyboardHookEventArgs>? KeyPressed;
        public event EventHandler<KeyboardHookEventArgs>? KeyReleased;
        public event EventHandler<MouseHookEventArgs>? MousePressed;
        public event EventHandler<MouseHookEventArgs>? MouseMoved;
        public event EventHandler<MouseWheelHookEventArgs>? MouseWheel;

        public void Start()
        {
            if (_globalHook != null) return;

            _globalHook = new TaskPoolGlobalHook();

            // SharpHook의 이벤트를 그대로 토스합니다.
            _globalHook.KeyPressed += OnHookKeyPressed;
            _globalHook.KeyReleased += OnHookKeyReleased;
            _globalHook.MousePressed += OnHookMousePressed;
            _globalHook.MouseMoved += OnHookMouseMoved;
            _globalHook.MouseWheel += OnHookMouseWheel;

        

            Task.Run(() => _globalHook.Run());
        }

        public void Stop()
        {
            if (_globalHook != null)
            {
            
                _globalHook.KeyPressed -= OnHookKeyPressed;
                _globalHook.KeyReleased -= OnHookKeyReleased;
                _globalHook.MousePressed -= OnHookMousePressed;
                _globalHook.MouseMoved -= OnHookMouseMoved;
                _globalHook.MouseWheel -= OnHookMouseWheel;

                _globalHook.Dispose();
                _globalHook = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void OnHookKeyPressed(object? sender, KeyboardHookEventArgs e) => KeyPressed?.Invoke(this, e);
        private void OnHookKeyReleased(object? sender, KeyboardHookEventArgs e) => KeyReleased?.Invoke(this, e);
        private void OnHookMousePressed(object? sender, MouseHookEventArgs e) => MousePressed?.Invoke(this, e);
        private void OnHookMouseMoved(object? sender, MouseHookEventArgs e) => MouseMoved?.Invoke(this, e);
        private void OnHookMouseWheel(object? sender, MouseWheelHookEventArgs e) => MouseWheel?.Invoke(this, e);
    }
}