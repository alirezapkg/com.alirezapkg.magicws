using System;

namespace MagicWS.Drivers
{
    public interface IWebSocketDriver
    {
        event Action OnOpen;
        event Action<byte[]> OnMessage;
        event Action OnClose;
        event Action<string> OnError;

        bool IsConnected { get; }
        string LocalAddr { get; }

        void Connect(string url);
        void Send(byte[] data);
        void Close();
        void Tick();
    }
}