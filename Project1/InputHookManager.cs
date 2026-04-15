using SharpHook;
using SharpHook.Native; // KeyCode 형식을 인식하기 위해 유지
using System;
using System.Threading.Tasks;

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

            // TaskPoolGlobalHook은 별도의 스레드에서 후킹을 처리합니다.
            _globalHook = new TaskPoolGlobalHook();

            // 💡 핵심 수정: 이벤트 연결 시 발생할 수 있는 타입 모호성을 해결합니다.
            _globalHook.KeyPressed += (s, e) => KeyPressed?.Invoke(this, e);
            _globalHook.KeyReleased += (s, e) => KeyReleased?.Invoke(this, e);
            _globalHook.MousePressed += (s, e) => MousePressed?.Invoke(this, e);
            _globalHook.MouseMoved += (s, e) => MouseMoved?.Invoke(this, e);

            // 비동기로 후킹 엔진 시작
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