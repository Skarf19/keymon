using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Keymon
{
    // UnityBridge의 역할:
    //   - KeyboardCat.exe (Unity 프로세스)를 실행하고 생명주기를 관리합니다.
    //   - UDP를 통해 현재 집중 상태(0~4)를 Unity에 전송합니다.
    //
    // IDisposable 인터페이스를 구현합니다.
    // IDisposable은 "이 클래스는 사용 후 반드시 Dispose()를 호출해야 한다"는 약속입니다.
    // 파일, 네트워크, 프로세스 등 OS 자원을 쓸 때는 항상 Dispose()로 자원을 반납해야 합니다.
    public class UnityBridge : IDisposable
    {
        // Process: 외부 프로그램(KeyboardCat.exe)을 실행하고 제어하는 클래스
        private Process? _process;

        // UdpClient: UDP 소켓 통신 클라이언트
        // UDP는 TCP와 달리 연결을 맺지 않고 데이터를 전송합니다. 속도가 빠르고 가볍습니다.
        private UdpClient? _udpSender;

        // IPEndPoint: 통신 대상의 IP 주소와 포트를 묶은 객체
        private IPEndPoint? _endpoint;

        // 마지막으로 Unity에 전송한 상태값을 기억합니다.
        // 상태가 바뀔 때만 전송하기 위한 중복 방지용입니다.
        private int _lastSentState = -1;

        // Unity 프로세스를 시작하고 UDP 통신을 준비합니다.
        public void Start()
        {
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unity", "KeyboardCat.exe");

            // File.Exists: 파일이 실제로 존재하는지 확인합니다.
            if (!File.Exists(exePath)) return;

            _udpSender = new UdpClient();

            // IPAddress.Loopback = 127.0.0.1 (자기 자신 컴퓨터를 가리키는 주소)
            // 포트 5000은 Unity 측과 약속한 번호입니다.
            _endpoint = new IPEndPoint(IPAddress.Loopback, 5000);

            _process = new Process();
            _process.StartInfo.FileName = exePath;
            _process.Start();
        }

        // 집중 상태(0~4)를 Unity에 UDP로 전송합니다.
        // 이전과 동일한 상태면 전송하지 않습니다 (불필요한 네트워크 트래픽 방지).
        public void SendState(int state)
        {
            if (_udpSender == null || _endpoint == null || state == _lastSentState) return;

            try
            {
                // Encoding.UTF8.GetBytes: 문자열을 UTF-8 바이트 배열로 변환합니다.
                // UDP는 바이트 배열을 전송하기 때문에 변환이 필요합니다.
                byte[] data = Encoding.UTF8.GetBytes(state.ToString());
                _udpSender.Send(data, data.Length, _endpoint);
                _lastSentState = state;
            }
            catch { }
        }

        // App이 종료될 때 호출됩니다.
        // Unity 프로세스를 강제 종료하고 UDP 소켓을 닫습니다.
        public void Dispose()
        {
            // Unity가 이미 종료되어 있을 수 있으므로 예외를 무시합니다.
            try { _process?.Kill(); } catch { }
            _udpSender?.Close();
        }
    }
}
