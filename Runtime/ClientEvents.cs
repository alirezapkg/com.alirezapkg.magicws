using System;
using System.Collections.Generic;

namespace MagicWS
{
    public class ClientEvents
    {
        private readonly MagicClient _client;
        private readonly Dictionary<uint, Action<byte[]>> _handlers = new Dictionary<uint, Action<byte[]>>();

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

        public void Emit(string eventName, byte[] payload)
        {
            uint hash = FNV1a.Hash(eventName);
            byte[] packet = new byte[4 + payload.Length];

            FNV1a.WriteHeader(packet, hash, 0);
            Buffer.BlockCopy(payload, 0, packet, 4, payload.Length);

            _client.Send(packet);
        }

        private void HandleRawMessage(byte[] data)
        {
            if (data.Length < 4) return;

            uint hash = FNV1a.ReadHeader(data, 0);
            if (_handlers.TryGetValue(hash, out var handler))
            {
                byte[] payload = new byte[data.Length - 4];
                Buffer.BlockCopy(data, 4, payload, 0, payload.Length);
                handler.Invoke(payload);
            }
        }
    }
}