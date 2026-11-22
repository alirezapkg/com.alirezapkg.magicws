using System;
using System.Threading;
using System.Threading.Tasks;
using MagicWS.Drivers;

namespace MagicWS
{
    /// <summary>
    /// Lifecycle state of a <see cref="MagicClient"/>.
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting
    }

    /// <summary>
    /// High-level MagicWS client.
    /// </summary>
    public class MagicClient
    {
        private readonly IWebSocketDriver _driver;
        private Action<byte[]> _onMessage;
        private Action _onClose;
        private Action<string> _onError;

        private readonly object _gate = new object();
        private ConnectionState _state = ConnectionState.Disconnected;
        private bool _isClosed = true;
        private string _url;
        private ReconnectOptions _reconnectOptions;
        private int _reconnectAttempt;
        private bool _reconnectScheduled;

        private SynchronizationContext _syncContext;

        public ConnectionState State
        {
            get { lock (_gate) { return _state; } }
            private set { lock (_gate) { _state = value; } }
        }

        public bool IsConnected
        {
            get
            {
                lock (_gate)
                {
                    return !_isClosed && _driver != null && _driver.IsConnected;
                }
            }
        }

        public bool IsClosed
        {
            get { lock (_gate) { return _isClosed; } }
        }

        public MagicClient(IWebSocketDriver driver = null)
        {
            _syncContext = SynchronizationContext.Current;

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
            _driver.OnError += HandleError;
        }

        public void OnMessage(Action<byte[]> fn)
        {
            _onMessage = fn;
        }

        public void OnClose(Action fn)
        {
            _onClose = fn;
        }

        public event Action<string> OnError
        {
            add { _onError += value; }
            remove { _onError -= value; }
        }

        public event Action OnConnected;
        public event Action OnReconnecting;
        public event Action OnReconnected;

        public void Connect(string url)
        {
            Connect(url, null);
        }

        public void Connect(string url, ReconnectOptions options = null)
        {
            lock (_gate)
            {
                _url = url;
                _reconnectOptions = options;
                _reconnectAttempt = 0;
                _reconnectScheduled = false;
                _isClosed = false;
                _state = ConnectionState.Connecting;
            }

            _driver.Connect(url);
        }

        public void Reconnect()
        {
            lock (_gate)
            {
                if (!_isClosed || string.IsNullOrEmpty(_url)) return;
                _isClosed = false;
                _reconnectScheduled = false;
                _state = ConnectionState.Connecting;
            }
            _driver.Connect(_url);
        }

        private void HandleOpen()
        {
            byte[] versionPacket = new byte[] { Protocol.Version };
            _driver.Send(versionPacket);

            bool wasReconnecting;
            lock (_gate)
            {
                wasReconnecting = _state == ConnectionState.Reconnecting;
                _state = ConnectionState.Connected;
                _reconnectAttempt = 0;
                _reconnectScheduled = false;
            }

            if (wasReconnecting)
            {
                OnReconnected?.Invoke();
            }
            else
            {
                OnConnected?.Invoke();
            }
        }

        private void HandleMessage(byte[] data)
        {
            _onMessage?.Invoke(data);
        }

        private void HandleError(string message)
        {
            _onError?.Invoke(message);
        }

        private void HandleClose()
        {
            bool shouldReconnect = false;
            lock (_gate)
            {
                if (_isClosed && _state == ConnectionState.Disconnected)
                {
                    return;
                }

                _isClosed = true;

                if (_reconnectOptions != null && !_reconnectScheduled && ShouldReconnect())
                {
                    _reconnectScheduled = true;
                    _state = ConnectionState.Reconnecting;
                    shouldReconnect = true;
                }
                else if (_reconnectOptions == null)
                {
                    _state = ConnectionState.Disconnected;
                }
            }

            _onClose?.Invoke();

            if (shouldReconnect)
            {
                ScheduleReconnect();
            }
        }

        private bool ShouldReconnect()
        {
            if (_reconnectOptions == null) return false;
            if (_reconnectOptions.Forever) return true;
            return _reconnectAttempt < _reconnectOptions.MaxAttempts;
        }

        private void ScheduleReconnect()
        {
            int attempt;
            float delay;
            lock (_gate)
            {
                attempt = _reconnectAttempt;
                float initial = _reconnectOptions.InitialDelaySeconds;
                float mult = _reconnectOptions.BackoffMultiplier;
                float max = _reconnectOptions.MaxDelaySeconds;
                delay = Math.Min(initial * (float)Math.Pow(mult, attempt), max);
                _reconnectAttempt = attempt + 1;
            }

            OnReconnecting?.Invoke();

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(delay)).ConfigureAwait(false);

                PostToMainThread(() =>
                {
                    bool shouldConnect;
                    lock (_gate)
                    {
                        shouldConnect = !_isClosed || _state == ConnectionState.Reconnecting;
                        if (shouldConnect)
                        {
                            _isClosed = false;
                            _reconnectScheduled = false;
                            // Keep _state as Reconnecting so HandleOpen can
                            // detect the reconnect and fire OnReconnected.
                        }
                    }
                    if (shouldConnect)
                    {
                        _driver.Connect(_url);
                    }
                });
            });
        }

        private void PostToMainThread(Action action)
        {
            var ctx = _syncContext;
            if (ctx != null)
            {
                ctx.Post(_ => action(), null);
            }
            else
            {
                action();
            }
        }

        public void Send(byte[] data)
        {
            if (!IsConnected) return;
            _driver.Send(data);
        }

        public string LocalAddr()
        {
            return _driver != null ? _driver.LocalAddr : "";
        }

        public void Close()
        {
            Close(1000, "");
        }

        public void Close(int code = 1000, string reason = "")
        {
            bool wasClosed;
            lock (_gate)
            {
                wasClosed = _isClosed;
                _isClosed = true;
                _state = ConnectionState.Disconnected;
            }

            if (_driver != null)
            {
                _driver.Close(code, reason);
            }

            if (!wasClosed)
            {
                _onClose?.Invoke();
            }
        }

        public void Tick()
        {
            Tick(64);
        }

        public void Tick(int maxMessages)
        {
            if (_driver == null) return;
            _driver.Tick(maxMessages);
        }
    }
}