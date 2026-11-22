using System;
using System.Collections.Generic;

namespace MagicWS
{
    /// <summary>
    /// Delegate for zero-allocation span-based event handlers.
    /// </summary>
    public delegate void SpanPayloadHandler(ReadOnlySpan<byte> payload);

    public class ClientEvents
    {
        private readonly MagicClient _client;
        private readonly Dictionary<uint, Action<byte[]>> _handlers = new Dictionary<uint, Action<byte[]>>();
        private readonly Dictionary<uint, SpanPayloadHandler> _spanHandlers = new Dictionary<uint, SpanPayloadHandler>();

        public ClientEvents(MagicClient client)
        {
            _client = client;
            _client.OnMessage(HandleRawMessage);
        }

        public void On(string eventName, Action<byte[]> handler)
        {
            uint hash = FNV1a.Hash(eventName);
            _handlers[hash] = handler;
        }

        public void OnSpan(string eventName, SpanPayloadHandler handler)
        {
            uint hash = FNV1a.Hash(eventName);
            _spanHandlers[hash] = handler;
        }

        public void Emit(string eventName, byte[] payload)
        {
            uint hash = FNV1a.Hash(eventName);
            byte[] packet = new byte[4 + payload.Length];

            FNV1a.WriteHeader(packet, hash, 0);
            Buffer.BlockCopy(payload, 0, packet, 4, payload.Length);

            _client.Send(packet);
        }

        public void Emit(string eventName, ReadOnlySpan<byte> payload)
        {
            uint hash = FNV1a.Hash(eventName);
            byte[] packet = new byte[4 + payload.Length];

            FNV1a.WriteHeader(packet, hash, 0);
            payload.CopyTo(packet.AsSpan(4));

            _client.Send(packet);
        }

        private void HandleRawMessage(byte[] data)
        {
            if (data.Length < 4) return;

            uint hash = FNV1a.ReadHeader(data, 0);

            // Try span handlers first (zero-copy path).
            if (_spanHandlers.TryGetValue(hash, out var handler))
            {
                ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(data, 4, data.Length - 4);
                handler.Invoke(span);
                return;
            }

            // Fallback to byte[] handlers.
            if (_handlers.TryGetValue(hash, out var byteHandler))
            {
                byte[] payload = new byte[data.Length - 4];
                Buffer.BlockCopy(data, 4, payload, 0, payload.Length);
                byteHandler.Invoke(payload);
            }
        }
    }
}