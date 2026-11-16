# MagicWS — Unity Client

High-performance, lightweight binary WebSocket client for Unity.

MagicWS keeps the wire format tiny: a 32-bit event id followed by an opaque
payload. No JSON, no base64, no per-message type tags.

```text
+----------------------+-------------------+
| event id (u32, BE)   | payload (n bytes) |
+----------------------+-------------------+
```

## Features

- Binary protocol with FNV-1a event hashing
- Main-thread safe dispatch — background receive loop, `Tick()` on the main thread
- Native platforms (Windows / macOS / Linux / Android / iOS) via `ClientWebSocket`
- WebGL builds via a jslib bridge
- Pooled receive buffers (`ArrayPool`) to avoid per-message allocation

## Requirements

- Unity **2022.3** or newer
- No external packages

## Installation

Add via Unity Package Manager (UPM):

```text
https://github.com/alirezapkg/com.alirezapkg.magicws.git#v1.0.0
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.alirezapkg.magicws": "https://github.com/alirezapkg/com.alirezapkg.magicws.git#v1.0.0"
  }
}
```

An integration sample is included — import it from
**Window → Package Manager → MagicWS → Samples**.

## Quick start

```csharp
using System.Text;
using MagicWS;
using UnityEngine;

public class Chat : MonoBehaviour
{
    [SerializeField] private string serverUrl = "ws://127.0.0.1:8080/ws";

    private MagicClient _client;
    private ClientEvents _events;

    private void Start()
    {
        _client = new MagicClient();
        _events = new ClientEvents(_client);

        _events.On("chat_message", OnChatMessage);
        _events.On("player_move", OnPlayerMove);

        _client.OnClose(() => Debug.LogWarning("disconnected"));
        _client.Connect(serverUrl);
    }

    private void Update()
    {
        _client?.Tick();   // required every frame
    }

    private void OnChatMessage(byte[] payload)
    {
        Debug.Log(Encoding.UTF8.GetString(payload));
    }

    private void OnPlayerMove(byte[] payload)
    {
        Debug.Log(Encoding.UTF8.GetString(payload));
    }

    public void SendChat(string text)
    {
        if (_client.IsClosed) return;
        _events.Emit("chat_message", Encoding.UTF8.GetBytes(text));
    }

    private void OnDestroy()
    {
        _client?.Close();
    }
}
```

## Usage

### Events

`ClientEvents` maps names to handlers and does the hashing for you:

```csharp
_events.On("player_move", payload => { /* ... */ });
_events.Emit("player_move", payload);
```

Registering the same name twice replaces the previous handler. Incoming messages
shorter than the 4-byte header are ignored.

### `Tick()` matters

The native driver runs its receive loop on a background thread and queues frames
into a `ConcurrentQueue`. `Tick()` drains that queue and invokes your handlers,
so it must be called from a Unity message loop (`Update`, `FixedUpdate`, …).
Handlers therefore always run on the main thread and can touch Unity objects
safely.

Skipping `Tick()` means messages arrive but never fire.

### Connection lifecycle

```csharp
_client.Connect("ws://127.0.0.1:8080/ws");
_client.OnClose(() => { /* reconnect */ });
_client.Close();

bool closed = _client.IsClosed;
string local = _client.LocalAddr();
```

The client sends the one-byte protocol version (`0x01`) automatically as soon as
the socket opens, so you never touch the handshake.

`Connect` is `async void` under the hood and reports failures through `OnClose` —
there is no separate error callback on `MagicClient` itself.

### Working with raw ids

If you need the id itself — for example to compare against a server constant:

```csharp
uint id  = FNV1a.Hash("player_move");
uint got = FNV1a.ReadHeader(buffer, 0);
```

`FNV1a.WriteHeader(buffer, hash, offset)` writes it back. Prefer `ClientEvents`
unless you are implementing the framing yourself.

### WebGL

WebGL builds use the jslib driver automatically. Note that the WebGL driver
throws `"WebGL driver is only supported in WebGL builds."` if constructed in the
editor, so test in a real WebGL build rather than the editor. Your server must
also allow the browser origin (CORS headers).

## Protocol

| | This client | Rust server | Go server |
|---|---|---|---|
| Version byte | `0x01` | `0x01` | `0x01` |
| Frame | `[u32 BE][payload]` | same | same |
| Hash | FNV-1a 32 | same | same |

Event ids are the **32-bit FNV-1a** hash of the event name:

```text
hash = 0x811c9dc5
for each char c of name:
    hash = hash XOR c
    hash = hash * 0x01000193   (wrapping)
```

Names are ASCII in practice, so every implementation agrees. Keep names in sync
between client and server.

## Server

This client is built for the MagicWS server:

| Server | Repository | Install |
|---|---|---|
| **Rust** (recommended) | [magic-ws](https://github.com/alirezapkg/magic-ws) | `cargo add magic-ws` |
| Go (legacy) | [magic-ws](https://github.com/alirezapkg/magic-ws) @ `v1.0.0` | tagged release |

Both speak the identical protocol, so a Unity build works against either.

## License

MIT — see [LICENSE](LICENSE).

Made with ❤️ by ali reza