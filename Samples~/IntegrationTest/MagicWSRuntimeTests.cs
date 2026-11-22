using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MagicWS;
using MagicWS.Drivers;
using UnityEngine;

namespace MagicWS.Testing
{
    /// <summary>
    /// Lightweight EditMode-style checks that run without NUnit so they work in
    /// any play-mode session. Attach to a GameObject and press Play.
    /// </summary>
    public class MagicWSRuntimeTests : MonoBehaviour
    {
        private int _passed;
        private int _failed;
        private readonly List<string> _failures = new List<string>();

        private void Start()
        {
            StartCoroutine(RunAllTests());
        }

        private System.Collections.IEnumerator RunAllTests()
        {
            RunStateTransitions();
            RunOnErrorOnce();
            RunLocalAddr();
            RunIsConnectedVsIsClosed();
            RunCloseWithCode();
            RunReconnectOptionsDefaults();
            RunTickBudget();
            RunConcurrentSend();
            yield return StartCoroutine(RunReconnectAfterRemoteClose());
            RunSpanEvents();
            RunSpanEmit();

            Debug.Log(
                $"[MagicWS Tests] Passed: {_passed}, Failed: {_failed}");
            foreach (string f in _failures)
            {
                Debug.LogError("[MagicWS Tests] FAIL: " + f);
            }
        }

        private void Check(string name, bool condition)
        {
            if (condition)
            {
                _passed++;
            }
            else
            {
                _failed++;
                _failures.Add(name);
            }
        }

        private void RunStateTransitions()
        {
            FakeWebSocketDriver fake = new FakeWebSocketDriver();
            MagicClient client = new MagicClient(fake);

            Check("initial state is disconnected",
                client.State == ConnectionState.Disconnected);
            Check("initially closed", client.IsClosed);

            client.Connect("ws://localhost:8080/ws");
            Check("connecting -> connected",
                client.State == ConnectionState.Connected);
            Check("is connected after open", client.IsConnected);
            Check("not closed after open", !client.IsClosed);

            client.Close();
            Check("closed after close", client.IsClosed);
            Check("disconnected after close",
                client.State == ConnectionState.Disconnected);
        }

        private void RunOnErrorOnce()
        {
            FakeWebSocketDriver fake = new FakeWebSocketDriver();
            MagicClient client = new MagicClient(fake);
            int count = 0;
            string last = null;
            client.OnError += msg => { count++; last = msg; };

            fake.TriggerError("boom");
            Check("on error fires once", count == 1);
            Check("on error message", last == "boom");
        }

        private void RunLocalAddr()
        {
            FakeWebSocketDriver fake = new FakeWebSocketDriver();
            MagicClient client = new MagicClient(fake);
            Check("local addr from driver",
                client.LocalAddr() == "fake-local-addr");
        }

        private void RunIsConnectedVsIsClosed()
        {
            FakeWebSocketDriver fake = new FakeWebSocketDriver();
            MagicClient client = new MagicClient(fake);

            Check("before connect: not connected", !client.IsConnected);
            Check("before connect: closed", client.IsClosed);

            client.Connect("ws://x/ws");
            Check("after open: connected", client.IsConnected);
            Check("after open: not closed", !client.IsClosed);

            client.Close();
            Check("after close: not connected", !client.IsConnected);
            Check("after close: closed", client.IsClosed);
        }

        private void RunCloseWithCode()
        {
            FakeWebSocketDriver fake = new FakeWebSocketDriver();
            MagicClient client = new MagicClient(fake);
            client.Connect("ws://x/ws");

            client.Close(4001, "bye");
            Check("close code forwarded", fake.LastCloseCode == 4001);
            Check("close reason forwarded", fake.LastCloseReason == "bye");
        }

        private void RunReconnectOptionsDefaults()
        {
            ReconnectOptions opts = new ReconnectOptions();
            Check("max attempts default", opts.MaxAttempts == 5);
            Check("initial delay default", opts.InitialDelaySeconds == 0.5f);
            Check("max delay default", opts.MaxDelaySeconds == 10f);
            Check("backoff multiplier default", opts.BackoffMultiplier == 2f);
            Check("forever default", !opts.Forever);
        }

        private void RunTickBudget()
        {
            FakeWebSocketDriver fake = new FakeWebSocketDriver();
            MagicClient client = new MagicClient(fake);
            client.Connect("ws://x/ws");

            int handled = 0;
            client.OnMessage(_ => handled++);

            for (int i = 0; i < 200; i++)
            {
                fake.Push(new byte[] { 0 });
            }

            client.Tick(10);
            Check("tick processes at most 10", handled == 10);

            client.Tick(10);
            Check("tick processes next 10", handled == 20);
        }

        private void RunConcurrentSend()
        {
            FakeWebSocketDriver fake = new FakeWebSocketDriver();
            MagicClient client = new MagicClient(fake);
            client.Connect("ws://x/ws");

            Exception error = null;
            const int threads = 16;
            const int perThread = 200;
            List<Thread> workers = new List<Thread>();
            for (int t = 0; t < threads; t++)
            {
                Thread worker = new Thread(() =>
                {
                    try
                    {
                        for (int i = 0; i < perThread; i++)
                        {
                            client.Send(new byte[] { (byte)i });
                        }
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                });
                workers.Add(worker);
                worker.Start();
            }
            foreach (Thread w in workers)
            {
                w.Join();
            }
            Check("concurrent send does not throw", error == null);
        }

        private System.Collections.IEnumerator RunReconnectAfterRemoteClose()
        {
            FakeWebSocketDriver fake = new FakeWebSocketDriver();
            MagicClient client = new MagicClient(fake);
            int reconnecting = 0;
            client.OnReconnecting += () => reconnecting++;
            client.OnReconnected += () => reconnecting += 100;

            client.Connect("ws://x/ws", new ReconnectOptions
            {
                MaxAttempts = 2,
                InitialDelaySeconds = 0.01f,
                MaxDelaySeconds = 0.01f,
                Forever = false,
            });

            // Server drops us; the client should schedule a reconnect.
            fake.SimulateRemoteClose();

            // Wait up to 2 seconds for reconnect to complete, yielding to main thread.
            float deadline = Time.realtimeSinceStartup + 2f;
            while (reconnecting < 101 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Check("on reconnecting fired", reconnecting > 0);
            // 1 reconnecting + 100 on reconnected = 101
            Check("on reconnected fired", reconnecting >= 101);
        }

        private void RunSpanEvents()
        {
            FakeWebSocketDriver fake = new FakeWebSocketDriver();
            MagicClient client = new MagicClient(fake);
            client.Connect("ws://x/ws");

            bool spanCalled = false;
            int spanLength = 0;
            ClientEvents events = new ClientEvents(client);
            events.OnSpan("span_event", payload =>
            {
                spanCalled = true;
                spanLength = payload.Length;
            });

            // Build a MagicWS frame: 4-byte header (event id) + payload.
            uint hash = FNV1a.Hash("span_event");
            byte[] frame = new byte[4 + 5];
            FNV1a.WriteHeader(frame, hash, 0);
            frame[4] = 72; frame[5] = 101; frame[6] = 108; frame[7] = 108; frame[8] = 111;
            fake.Push(frame);

            client.Tick();
            Check("span handler called", spanCalled);
            Check("span payload length", spanLength == 5);
        }

        private void RunSpanEmit()
        {
            FakeWebSocketDriver fake = new FakeWebSocketDriver();
            MagicClient client = new MagicClient(fake);
            client.Connect("ws://x/ws");

            ClientEvents events = new ClientEvents(client);

            // Span-based emit should not throw.
            byte[] payload = new byte[] { 1, 2, 3, 4 };
            events.Emit("span_test", payload.AsSpan());
            Check("span emit does not throw", true);
        }
    }

    /// <summary>
    /// In-memory driver for tests.
    /// </summary>
    public class FakeWebSocketDriver : IWebSocketDriver
    {
        private readonly ConcurrentQueue<byte[]> _receiveQueue =
            new ConcurrentQueue<byte[]>();

        public event Action OnOpen;
        public event Action<byte[]> OnMessage;
        public event Action OnClose;
        public event Action<string> OnError;

        public bool IsConnected { get; private set; }
        public string LocalAddr { get { return "fake-local-addr"; } }
        public int LastCloseCode { get; private set; }
        public string LastCloseReason { get; private set; }

        public void Connect(string url)
        {
            IsConnected = true;
            OnOpen?.Invoke();
        }

        public void Send(byte[] data) { }

        public void Push(byte[] data)
        {
            _receiveQueue.Enqueue(data);
        }

        public void TriggerError(string message)
        {
            OnError?.Invoke(message);
        }

        /// <summary>Simulates the server ending the connection.</summary>
        public void SimulateRemoteClose()
        {
            IsConnected = false;
            OnClose?.Invoke();
        }

        public void Tick() { Tick(64); }

        public void Tick(int maxMessages)
        {
            int n = 0;
            while (n < maxMessages && _receiveQueue.TryDequeue(out byte[] data))
            {
                OnMessage?.Invoke(data);
                n++;
            }
        }

        public void Close() { Close(1000, ""); }

        public void Close(int code, string reason)
        {
            LastCloseCode = code;
            LastCloseReason = reason;
            IsConnected = false;
        }
    }
}