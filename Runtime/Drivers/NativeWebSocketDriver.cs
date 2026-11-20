using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace MagicWS.Drivers
{
    /// <summary>
    /// Native ClientWebSocket driver. A single writer task drains the send
    /// queue, so Send is safe to call from any thread and message order is
    /// preserved.
    /// </summary>
    public class NativeWebSocketDriver : IWebSocketDriver
    {
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private BlockingCollection<byte[]> _sendQueue;
        private Task _writerTask;
        private readonly ConcurrentQueue<byte[]> _receiveQueue = new ConcurrentQueue<byte[]>();
        private int _sendQueueSize = 256;
        private bool _dropOnFull = true;

        public event Action OnOpen;
        public event Action<byte[]> OnMessage;
        public event Action OnClose;
        public event Action<string> OnError;

        public bool IsConnected
        {
            get { return _ws != null && _ws.State == WebSocketState.Open; }
        }

        /// <summary>
        /// Local endpoint of the underlying socket. Some runtimes do not expose
        /// it, in which case an empty string is returned.
        /// </summary>
        public string LocalAddr
        {
            get
            {
                if (_ws == null) return "";
                try
                {
                    const BindingFlags flags =
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    Type type = _ws.GetType();
                    PropertyInfo prop = type.GetProperty("LocalEndPoint", flags) ??
                                        type.GetProperty("ListenerEndPoint", flags);
                    EndPoint value = prop != null ? prop.GetValue(_ws) as EndPoint : null;
                    return value != null ? value.ToString() : "";
                }
                catch
                {
                    return "";
                }
            }
        }

        public void Connect(string url)
        {
            if (IsConnected) return;

            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            _sendQueue = new BlockingCollection<byte[]>(
                new ConcurrentQueue<byte[]>(), _sendQueueSize);
            _writerTask = Task.Run(() => WriterLoop(_sendQueue, _cts.Token));

            ConnectAsync(url);
        }

        private async void ConnectAsync(string url)
        {
            try
            {
                await _ws.ConnectAsync(new Uri(url), _cts.Token).ConfigureAwait(false);
                OnOpen?.Invoke();
                _ = ReceiveLoop();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex.Message);
                OnClose?.Invoke();
            }
        }

        private async Task WriterLoop(BlockingCollection<byte[]> queue, CancellationToken token)
        {
            try
            {
                foreach (byte[] data in queue.GetConsumingEnumerable(token))
                {
                    await _ws.SendAsync(
                        new ArraySegment<byte>(data),
                        WebSocketMessageType.Binary,
                        true,
                        token).ConfigureAwait(false);
                }
            }
            catch
            {
                // The receive loop reports closure.
            }
        }

        private async Task ReceiveLoop()
        {
            try
            {
                while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    byte[] receiveBuffer = ArrayPool<byte>.Shared.Rent(4096);
                    int totalBytes = 0;

                    while (true)
                    {
                        ArraySegment<byte> segment = new ArraySegment<byte>(
                            receiveBuffer, totalBytes, receiveBuffer.Length - totalBytes);
                        WebSocketReceiveResult result =
                            await _ws.ReceiveAsync(segment, _cts.Token).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            ArrayPool<byte>.Shared.Return(receiveBuffer);
                            await _ws.CloseAsync(
                                WebSocketCloseStatus.NormalClosure, "Close", _cts.Token)
                                .ConfigureAwait(false);
                            return;
                        }

                        totalBytes += result.Count;

                        if (result.EndOfMessage) break;

                        if (totalBytes >= receiveBuffer.Length)
                        {
                            byte[] nextBuffer = ArrayPool<byte>.Shared.Rent(receiveBuffer.Length * 2);
                            Buffer.BlockCopy(receiveBuffer, 0, nextBuffer, 0, totalBytes);
                            ArrayPool<byte>.Shared.Return(receiveBuffer);
                            receiveBuffer = nextBuffer;
                        }
                    }

                    if (totalBytes > 0)
                    {
                        byte[] exactData = new byte[totalBytes];
                        Buffer.BlockCopy(receiveBuffer, 0, exactData, 0, totalBytes);
                        _receiveQueue.Enqueue(exactData);
                    }

                    ArrayPool<byte>.Shared.Return(receiveBuffer);
                }
            }
            catch
            {
                // Silence stream termination exceptions on close.
            }
            finally
            {
                OnClose?.Invoke();
            }
        }

        public void Send(byte[] data)
        {
            if (!IsConnected || _sendQueue == null) return;

            if (!_sendQueue.TryAdd(data))
            {
                if (_dropOnFull)
                {
                    return;
                }
                _sendQueue.Add(data);
            }
        }

        public void Send(byte[] data, SendOptions options)
        {
            if (options != null)
            {
                _sendQueueSize = options.QueueSize;
                _dropOnFull = options.DropOnFull;
            }
            Send(data);
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
            if (_cts != null)
            {
                _cts.Cancel();
            }

            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try
                {
                    _ws.CloseAsync(
                        (WebSocketCloseStatus)code, reason, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Ignore disconnect races.
                }
            }

            if (_sendQueue != null)
            {
                _sendQueue.CompleteAdding();
            }

            if (_ws != null)
            {
                _ws.Dispose();
                _ws = null;
            }
        }
    }
}