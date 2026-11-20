using System;

namespace MagicWS.Drivers
{
    public class SendOptions
    {
        public int QueueSize = 256;
        public bool DropOnFull = true;
    }

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
        void Close(int code, string reason);
        void Tick();
        void Tick(int maxMessages);
    }
}