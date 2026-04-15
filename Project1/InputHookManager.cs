using SharpHook;
using SharpHook.Native; // 네이티브 이벤트 핸들링을 위해 필요
using System;
using System.Threading.Tasks;

namespace Project1
{
    public class InputHookManager : IDisposable
    {
        private TaskPoolGlobalHook? _globalHook;

        // 💡 중요: 이벤트 인자의 데이터 타입을 명확히 하기 위해 SharpHook.Native.KeyCode를 직접 언급하지 않는 방향으로 안전하게 설계합니다.
        public event EventHandler<KeyboardHookEventArgs>? KeyPressed;
        public event EventHandler<KeyboardHookEventArgs>? KeyReleased;
        public event EventHandler<MouseHookEventArgs>? MousePressed;
        public event EventHandler<MouseHookEventArgs>? MouseMoved;

        public void Start()
        {
            if (_globalHook != null) return;

            _globalHook = new TaskPoolGlobalHook();

            // SharpHook의 이벤트를 그대로 토스합니다.
            _globalHook.KeyPressed += (s, e) => KeyPressed?.Invoke(this, e);
            _globalHook.KeyReleased += (s, e) => KeyReleased?.Invoke(this, e);
            _globalHook.MousePressed += (s, e) => MousePressed?.Invoke(this, e);
            _globalHook.MouseMoved += (s, e) => MouseMoved?.Invoke(this, e);

            // 비동기로 실행
            Task.Run(() => _globalHook.Run());
        }

        public void Stop()
        {
            if (_globalHook != null)
            {
                _globalHook.Dispose();
                _globalHook = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}