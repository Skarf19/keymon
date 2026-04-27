using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Test
{
    public partial class Form1
    {
        private UdpClient udpSender;
        private IPEndPoint unityEndPoint;
        private Process unityProcess;

        private void LaunchUnity()
        {
            udpSender = new UdpClient();
            unityEndPoint = new IPEndPoint(IPAddress.Loopback, 5000);

            string unityExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KeyboardCat.exe");
            if (File.Exists(unityExePath))
            {
                unityProcess = new Process();
                unityProcess.StartInfo.FileName = unityExePath;
                unityProcess.Start();
            }
        }

        private void SendToUnity(string message)
        {
            try
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
                udpSender.Send(data, data.Length, unityEndPoint);
            }
            catch { }
        }

        private void SetStartup()
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                key.SetValue("KeyboardAnalyzer", Application.ExecutablePath);
                key.Close();
            }
            catch { }
        }
    }
}