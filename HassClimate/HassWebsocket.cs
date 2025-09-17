// Requires: WebSocket4Net, Newtonsoft.Json
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SuperSocket.ClientEngine; // for ErrorEventArgs
using System;
using WebSocket4Net;

public class HassWebSocket : IDisposable
{
    readonly string _wsUrl;   // e.g. ws://ha.local:8123/api/websocket
    readonly string _token;   // HA long-lived token
    WebSocket _ws;
    int _msgId = 1;
    bool _disposed;

    public event Action Connected;
    public event Action Disconnected;
    public event Action<string, JObject> EventReceived; // (event_type, json)
    public event Action<string> Error;
    public event Action<JObject> StateChanged;
    public event Action<JArray> InitialStates;

    public HassWebSocket(string wsUrl, string token)
    {
        _wsUrl = wsUrl;
        _token = token;
    }

    public void Connect()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HassWebSocket));
        if (_ws != null && _ws.State == WebSocketState.Open) return;

        _ws = new WebSocket(_wsUrl)
        {
            EnableAutoSendPing = true,
            AutoSendPingInterval = 10
        };

        _ws.Opened += (s, e) => { /* wait for auth_required */ };
        _ws.Closed += (s, e) => { Disconnected?.Invoke(); };
        _ws.Error += (object s, ErrorEventArgs e) =>
        {
            Error?.Invoke(e.Exception?.Message ?? "ws error");
        };
        _ws.MessageReceived += (s, e) => OnMessage(e.Message);

        _ws.Open();
    }

    public void Disconnect()
    {
        try { _ws?.Close(); } catch { }
        _ws = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }

    void OnMessage(string msg)
    {
        JObject o;
        try { o = JObject.Parse(msg); }
        catch { Error?.Invoke("bad json"); return; }

        var type = (string)o["type"] ?? "";
        if (type == "auth_required")
        {
            Send(new JObject { ["type"] = "auth", ["access_token"] = _token });
            return;
        }
        if (type == "auth_ok")
        {
            Connected?.Invoke();
            GetStates();
            Subscribe("state_changed");
            return;
        }
        if (type == "event")
        {
            var ev = (JObject)o["event"];
            var evType = (string)ev?["event_type"];
            EventReceived?.Invoke(evType, ev);
            if (evType == "state_changed") StateChanged?.Invoke(ev);
            return;
        }
        if (type == "result")
        {
            var success = (bool?)o["success"] ?? false;
            var id = (int?)o["id"] ?? -1;
            if (id == _lastGetStatesId && success)
            {
                var r = (JArray)o["result"];
                InitialStates?.Invoke(r);
            }
            return;
        }
    }

    int _lastGetStatesId = -1;

    void Subscribe(string eventType)
    {
        Send(new JObject
        {
            ["id"] = NextId(),
            ["type"] = "subscribe_events",
            ["event_type"] = eventType
        });
    }

    public void GetStates()
    {
        _lastGetStatesId = NextId();
        Send(new JObject { ["id"] = _lastGetStatesId, ["type"] = "get_states" });
    }

    public void CallClimateService(string service, string entityId, JObject data = null)
    {
        if (string.IsNullOrEmpty(service) || string.IsNullOrEmpty(entityId)) return;
        var obj = new JObject
        {
            ["id"] = NextId(),
            ["type"] = "call_service",
            ["domain"] = "climate",
            ["service"] = service,
            ["target"] = new JObject { ["entity_id"] = entityId },
            ["service_data"] = data ?? new JObject()
        };
        Send(obj);
    }

    int NextId() => System.Threading.Interlocked.Increment(ref _msgId);

    void Send(JObject o)
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
        {
            Error?.Invoke("send while closed");
            return;
        }
        _ws.Send(o.ToString(Formatting.None));
    }
}
