using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;

namespace MagicWS.Drivers
{
    /// <summary>
    /// WebGL jslib driver. Each instance owns a unique connection id so
    /// multiple <see cref="MagicClient"/> instances can coexist.
    /// </summary>
    public class WebGLWebSocketDriver : IWebSocketDriver
    {
        private static readonly Dictionary<int, WebGLWebSocketDriver> _instances =
            new Dictionary<int, WebGLWebSocketDriver>();

        private int _connectionId;
        private readonly ConcurrentQueue<byte[]> _receiveQueue = new ConcurrentQueue<byte[]>();

        public event Action OnOpen;
        public event Action<byte[]> OnMessage;
        public event Action OnClose;
        public event Action<string> OnError;

        public bool IsConnected { get; private set; }
        public string LocalAddr { get { return "webgl-client:" + _connectionId; } }

        private delegate void DelegateNoParam(int connectionId);
        private delegate void DelegateMessageParam(int connectionId, IntPtr bufferPtr, int length);

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int MagicWS_Connect(
            string url,
            DelegateNoParam onOpen,
            DelegateMessageParam onMessage,
            DelegateNoParam onClose,
            DelegateNoParam onError);

        [DllImport("__Internal")]
        private static extern void MagicWS_Send(int id, byte[] ptr, int length);

        [DllImport("__Internal")]
        private static extern void MagicWS_Close(int id);

        [DllImport("__Internal")]
        private static extern void MagicWS_CloseWithCode(int id, int code, string reason);
#endif

        public WebGLWebSocketDriver()
        {
        }

        public void Connect(string url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            int id = MagicWS_Connect(
                url,
                JS_OnOpen,
                JS_OnMessage,
                JS_OnClose,
                JS_OnError);
            _connectionId = id;
            lock (_instances)
            {
                _instances[_connectionId] = this;
            }
#else
            OnError?.Invoke("WebGL driver is only supported in WebGL builds.");
            OnClose?.Invoke();
#endif
        }

        public void Send(byte[] data)
        {
            if (!IsConnected) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            MagicWS_Send(_connectionId, data, data.Length);
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
            if (code == 1000 && string.IsNullOrEmpty(reason))
            {
                MagicWS_Close(_connectionId);
            }
            else
            {
                MagicWS_CloseWithCode(_connectionId, code, reason ?? "");
            }
#endif
            OnClose?.Invoke();
        }

        [MonoPInvokeCallback(typeof(DelegateNoParam))]
        private static void JS_OnOpen(int connectionId)
        {
            WebGLWebSocketDriver instance = FindInstance(connectionId);
            if (instance == null) return;
            instance.IsConnected = true;
            instance.OnOpen?.Invoke();
        }

        [MonoPInvokeCallback(typeof(DelegateMessageParam))]
        private static void JS_OnMessage(int connectionId, IntPtr bufferPtr, int length)
        {
            WebGLWebSocketDriver instance = FindInstance(connectionId);
            if (instance == null || length <= 0) return;

            byte[] data = new byte[length];
            Marshal.Copy(bufferPtr, data, 0, length);
            instance._receiveQueue.Enqueue(data);
        }

        [MonoPInvokeCallback(typeof(DelegateNoParam))]
        private static void JS_OnClose(int connectionId)
        {
            WebGLWebSocketDriver instance = FindInstance(connectionId);
            if (instance == null) return;
            instance.IsConnected = false;
            instance.OnClose?.Invoke();
        }

        [MonoPInvokeCallback(typeof(DelegateNoParam))]
        private static void JS_OnError(int connectionId)
        {
            WebGLWebSocketDriver instance = FindInstance(connectionId);
            if (instance == null) return;
            instance.OnError?.Invoke("WebSocket Error in WebGL");
        }

        private static WebGLWebSocketDriver FindInstance(int connectionId)
        {
            lock (_instances)
            {
                _instances.TryGetValue(connectionId, out var instance);
                return instance;
            }
        }
    }
}