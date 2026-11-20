using System.Text;
using UnityEngine;

namespace MagicWS.Testing
{
    public class MagicIntegrationTest : MonoBehaviour
    {
        [SerializeField] private string serverUrl = "ws://localhost:8080/ws";

        private MagicClient _client;
        private ClientEvents _events;

        private void Start()
        {
            _client = new MagicClient();
            _events = new ClientEvents(_client);

            _events.On("chat_message", OnChatMessageReceived);
            _events.On("player_move", OnPlayerMoveReceived);

            _client.OnClose(() =>
            {
                Debug.LogWarning("❌ Disconnected from MagicWS Server.");
            });

            Debug.Log($"Connecting to MagicWS server at {serverUrl}...");
            _client.Connect(serverUrl);
        }

        private void Update()
        {
            _client?.Tick();

            if (Input.GetKeyDown(KeyCode.Space))
            {
                SendMoveTest();
            }

            if (Input.GetKeyDown(KeyCode.C))
            {
                SendChatTest();
            }
        }

        public void SendMoveTest()
        {
            if (!_client.IsConnected) return;

            string moveData = $"x:{Random.Range(0, 100)},y:{Random.Range(0, 100)}";
            byte[] payload = Encoding.UTF8.GetBytes(moveData);

            Debug.Log($"[Unity -> Server] Sending Move: {moveData}");
            _events.Emit("player_move", payload);
        }

        public void SendChatTest()
        {
            if (!_client.IsConnected) return;

            string chatData = $"Hello from Unity client at {Time.time:F1}s!";
            byte[] payload = Encoding.UTF8.GetBytes(chatData);

            Debug.Log($"[Unity -> Server] Sending Chat: {chatData}");
            _events.Emit("chat_message", payload);
        }

        private void OnChatMessageReceived(byte[] payload)
        {
            string message = Encoding.UTF8.GetString(payload);
            Debug.Log($"<color=green>[Server -> Unity] Chat Received:</color> {message}");
        }

        private void OnPlayerMoveReceived(byte[] payload)
        {
            string moveInfo = Encoding.UTF8.GetString(payload);
            Debug.Log($"<color=cyan>[Server -> Unity] Move Received:</color> {moveInfo}");
        }

        private void OnDestroy()
        {
            _client?.Close();
        }
    }
}