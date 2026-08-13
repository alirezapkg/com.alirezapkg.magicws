using System;
using MagicWS.Drivers;

namespace MagicWS
{
    public class MagicClient
    {
        private readonly IWebSocketDriver _driver;
        private Action<byte[]> _onMessage;
        private Action _onClose;
        private bool _isClosed = true;

        public bool IsClosed => _isClosed || _driver == null || !_driver.IsConnected;

        public MagicClient(IWebSocketDriver driver = null)
        {
            if (driver == null)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                _driver = new WebGLWebSocketDriver();
#else
                _driver = new NativeWebSocketDriver();
#endif
            }
            else
            {
                _driver = driver;
            }

            _driver.OnOpen += HandleOpen;
            _driver.OnMessage += HandleMessage;
            _driver.OnClose += HandleClose;
        }

        public void OnMessage(Action<byte[]> fn)
        {
            _onMessage = fn;
        }

        public void OnClose(Action fn)
        {
            _onClose = fn;
        }

        public void Connect(string url)
        {
            _isClosed = false;
            _driver.Connect(url);
        }

        private void HandleOpen()
        {
            byte[] versionPacket = new byte[] { Protocol.Version };
            _driver.Send(versionPacket);
        }

        private void HandleMessage(byte[] data)
        {
            _onMessage?.Invoke(data);
        }

        private void HandleClose()
        {
            if (_isClosed) return;
            _isClosed = true;
            _onClose?.Invoke();
        }

        public void Send(byte[] data)
        {
            if (IsClosed) return;
            _driver.Send(data);
        }

        public string LocalAddr()
        {
            return _driver?.LocalAddr ?? "";
        }

        public void Close()
        {
            if (_isClosed) return;
            _isClosed = true;
            
            _driver?.Close();
            _onClose?.Invoke();
        }

        public void Tick()
        {
            _driver?.Tick();
        }
    }
}