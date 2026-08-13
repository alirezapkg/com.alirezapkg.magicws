using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace MagicWS.Drivers
{
    public class NativeWebSocketDriver : IWebSocketDriver
    {
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly ConcurrentQueue<byte[]> _receiveQueue = new ConcurrentQueue<byte[]>();

        public event Action OnOpen;
        public event Action<byte[]> OnMessage;
        public event Action OnClose;
        public event Action<string> OnError;

        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;
        public string LocalAddr => "127.0.0.1";

        public async void Connect(string url)
        {
            if (IsConnected) return;

            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            try
            {
                await _ws.ConnectAsync(new Uri(url), _cts.Token);
                OnOpen?.Invoke();

                _ = ReceiveLoop();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex.Message);
                OnClose?.Invoke();
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
                        var segment = new ArraySegment<byte>(receiveBuffer, totalBytes, receiveBuffer.Length - totalBytes);
                        var result = await _ws.ReceiveAsync(segment, _cts.Token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            ArrayPool<byte>.Shared.Return(receiveBuffer);
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Close", _cts.Token);
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
                // Silence stream termination exceptions on close
            }
            finally
            {
                OnClose?.Invoke();
            }
        }

        public async void Send(byte[] data)
        {
            if (!IsConnected) return;

            try
            {
                await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, _cts.Token);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex.Message);
            }
        }

        public void Tick()
        {
            while (_receiveQueue.TryDequeue(out byte[] data))
            {
                OnMessage?.Invoke(data);
            }
        }

        public async void Close()
        {
            _cts?.Cancel();
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed", CancellationToken.None);
                }
                catch
                {
                    // Ignore disconnect races
                }
            }
            _ws?.Dispose();
            _ws = null;
        }
    }
}