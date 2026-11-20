using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using AOT;

namespace MagicWS.Drivers
{
    /// <summary>
    /// WebGL jslib driver. Single connection per instance in this version.
    /// </summary>
    public class WebGLWebSocketDriver : IWebSocketDriver
    {
        private static WebGLWebSocketDriver _activeInstance;
        private readonly ConcurrentQueue<byte[]> _receiveQueue = new ConcurrentQueue<byte[]>();

        public event Action OnOpen;
        public event Action<byte[]> OnMessage;
        public event Action OnClose;
        public event Action<string> OnError;

        public bool IsConnected { get; private set; }
        public string LocalAddr { get { return "webgl-client"; } }

        private delegate void DelegateNoParam();
        private delegate void DelegateMessageParam(IntPtr bufferPtr, int length);

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void MagicWS_Connect(
            string url,
            DelegateNoParam onOpen,
            DelegateMessageParam onMessage,
            DelegateNoParam onClose,
            DelegateNoParam onError);

        [DllImport("__Internal")]
        private static extern void MagicWS_Send(byte[] ptr, int length);

        [DllImport("__Internal")]
        private static extern void MagicWS_Close();
#endif

        public WebGLWebSocketDriver()
        {
            _activeInstance = this;
        }

        public void Connect(string url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            MagicWS_Connect(
                url,
                JS_OnOpen,
                JS_OnMessage,
                JS_OnClose,
                JS_OnError);
#else
            OnError?.Invoke("WebGL driver is only supported in WebGL builds.");
            OnClose?.Invoke();
#endif
        }

        public void Send(byte[] data)
        {
            if (!IsConnected) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            MagicWS_Send(data, data.Length);
#endif
        }

        public void Tick()
        {
            Tick(64);
        }

        public void Tick(int maxMessages)
        {
            int n = 0;
            while (n < maxMessages && _receiveQueue.TryDequeue(out byte[] data))
            {
                OnMessage?.Invoke(data);
                n++;
            }
        }

        public void Close()
        {
            Close(1000, "");
        }

        public void Close(int code, string reason)
        {
            IsConnected = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            // Custom close codes are P1; P0 sends a normal closure.
            MagicWS_Close();
#endif
            OnClose?.Invoke();
        }

        [MonoPInvokeCallback(typeof(DelegateNoParam))]
        private static void JS_OnOpen()
        {
            if (_activeInstance == null) return;
            _activeInstance.IsConnected = true;
            _activeInstance.OnOpen?.Invoke();
        }

        [MonoPInvokeCallback(typeof(DelegateMessageParam))]
        private static void JS_OnMessage(IntPtr bufferPtr, int length)
        {
            if (_activeInstance == null || length <= 0) return;

            byte[] data = new byte[length];
            Marshal.Copy(bufferPtr, data, 0, length);
            _activeInstance._receiveQueue.Enqueue(data);
        }

        [MonoPInvokeCallback(typeof(DelegateNoParam))]
        private static void JS_OnClose()
        {
            if (_activeInstance == null) return;
            _activeInstance.IsConnected = false;
            _activeInstance.OnClose?.Invoke();
        }

        [MonoPInvokeCallback(typeof(DelegateNoParam))]
        private static void JS_OnError()
        {
            if (_activeInstance == null) return;
            _activeInstance.OnError?.Invoke("WebSocket Error in WebGL");
        }
    }
}