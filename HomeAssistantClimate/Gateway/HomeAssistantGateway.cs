using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace HomeAssistantClimate.Gateway
{
    public sealed class HomeAssistantGateway : IDisposable
    {
        private readonly Uri _webSocketUri;
        private readonly string _token;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JToken>> _pendingRequests = new ConcurrentDictionary<int, TaskCompletionSource<JToken>>();
        private readonly ConcurrentDictionary<string, ClimateDeviceState> _knownThermostats = new ConcurrentDictionary<string, ClimateDeviceState>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private ClientWebSocket _client;
        private CancellationTokenSource _internalTokenSource;
        private Task _receiveLoop;
        private int _messageId;
        private bool _disposed;

        public HomeAssistantGateway(Uri webSocketUri, string token)
        {
            _webSocketUri = webSocketUri ?? throw new ArgumentNullException(nameof(webSocketUri));
            _token = token ?? throw new ArgumentNullException(nameof(token));
        }

        public event EventHandler<bool> ConnectionStateChanged;
        public event EventHandler<ThermostatDiscoveredEventArgs> ThermostatDiscovered;
        public event EventHandler<ThermostatUpdatedEventArgs> ThermostatUpdated;
        public event EventHandler<ThermostatRemovedEventArgs> ThermostatRemoved;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                while (!linkedCts.IsCancellationRequested)
                {
                    try
                    {
                        await ConnectAndRunAsync(linkedCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception)
                    {
                        ConnectionStateChanged?.Invoke(this, false);
                        await Task.Delay(TimeSpan.FromSeconds(5), linkedCts.Token).ConfigureAwait(false);
                    }
                }
            }
        }

        public async Task SetTargetTemperatureAsync(string entityId, double? target, double? low, double? high, CancellationToken cancellationToken)
        {
            var data = new Dictionary<string, JToken>();
            if (target.HasValue)
            {
                data["temperature"] = JToken.FromObject(target.Value);
            }

            if (low.HasValue)
            {
                data["target_temp_low"] = JToken.FromObject(low.Value);
            }

            if (high.HasValue)
            {
                data["target_temp_high"] = JToken.FromObject(high.Value);
            }

            if (data.Count == 0)
            {
                return;
            }

            await SendServiceCommandAsync(entityId, "set_temperature", data, cancellationToken).ConfigureAwait(false);
        }

        public Task SetHvacModeAsync(string entityId, string mode, CancellationToken cancellationToken)
        {
            var data = new Dictionary<string, JToken>
            {
                { "hvac_mode", mode }
            };

            return SendServiceCommandAsync(entityId, "set_hvac_mode", data, cancellationToken);
        }

        public Task SetPresetModeAsync(string entityId, string preset, CancellationToken cancellationToken)
        {
            var data = new Dictionary<string, JToken>
            {
                { "preset_mode", preset }
            };

            return SendServiceCommandAsync(entityId, "set_preset_mode", data, cancellationToken);
        }

        public Task SetFanModeAsync(string entityId, string mode, CancellationToken cancellationToken)
        {
            var data = new Dictionary<string, JToken>
            {
                { "fan_mode", mode }
            };

            return SendServiceCommandAsync(entityId, "set_fan_mode", data, cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _internalTokenSource?.Cancel();
            _internalTokenSource?.Dispose();
            _client?.Dispose();
            _sendLock.Dispose();
        }

        private async Task ConnectAndRunAsync(CancellationToken cancellationToken)
        {
            _internalTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _knownThermostats.Clear();

            _client?.Dispose();
            _client = new ClientWebSocket();
            _client.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

            await _client.ConnectAsync(_webSocketUri, cancellationToken).ConfigureAwait(false);

            var authHandshake = await ReceiveMessageAsync(cancellationToken).ConfigureAwait(false);
            if (authHandshake == null)
            {
                throw new IOException("Connection closed during authentication");
            }

            var messageType = authHandshake.Value<string>("type");
            if (string.Equals(messageType, "auth_required", StringComparison.OrdinalIgnoreCase))
            {
                await SendAsync(HomeAssistantMessageFactory.CreateAuthMessage(_token), cancellationToken).ConfigureAwait(false);
                authHandshake = await ReceiveMessageAsync(cancellationToken).ConfigureAwait(false);
                messageType = authHandshake.Value<string>("type");
            }

            if (!string.Equals(messageType, "auth_ok", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(messageType, "auth_invalid", StringComparison.OrdinalIgnoreCase))
                {
                    var message = authHandshake.Value<string>("message") ?? "Authentication failed";
                    throw new InvalidOperationException(message);
                }

                throw new InvalidOperationException($"Unexpected authentication response '{messageType}'");
            }

            ConnectionStateChanged?.Invoke(this, true);

            _receiveLoop = Task.Run(() => ReceiveLoopAsync(_internalTokenSource.Token), _internalTokenSource.Token);

            await SendAsync(HomeAssistantMessageFactory.CreateSubscribeStateChangesMessage(GetMessageId()), cancellationToken).ConfigureAwait(false);

            var statesToken = await SendRequestAsync(HomeAssistantMessageFactory.CreateGetStatesMessage(GetMessageId()), cancellationToken).ConfigureAwait(false);
            if (statesToken is JArray array)
            {
                ProcessInitialStates(array);
            }

            await _receiveLoop.ConfigureAwait(false);
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var message = await ReceiveMessageAsync(cancellationToken).ConfigureAwait(false);
                    if (message == null)
                    {
                        break;
                    }

                    var type = message.Value<string>("type");
                    switch (type)
                    {
                        case "event":
                            HandleEventMessage(message);
                            break;
                        case "result":
                            HandleResultMessage(message);
                            break;
                        case "pong":
                            break;
                        default:
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                ConnectionStateChanged?.Invoke(this, false);
                _internalTokenSource?.Cancel();
            }
        }

        private void HandleResultMessage(JObject message)
        {
            var id = message.Value<int?>("id");
            if (id == null)
            {
                return;
            }

            if (_pendingRequests.TryRemove(id.Value, out var tcs))
            {
                var success = message.Value<bool?>("success") ?? true;
                if (!success)
                {
                    var error = message.Value<JObject>("error");
                    tcs.TrySetException(new InvalidOperationException(error?.Value<string>("message") ?? "Home Assistant error"));
                }
                else
                {
                    tcs.TrySetResult(message["result"]);
                }
            }
        }

        private void HandleEventMessage(JObject message)
        {
            var @event = message.Value<JObject>("event");
            if (@event == null)
            {
                return;
            }

            var eventType = @event.Value<string>("event_type");
            if (!string.Equals(eventType, "state_changed", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var data = @event.Value<JObject>("data");
            var entityId = data?.Value<string>("entity_id");
            if (string.IsNullOrEmpty(entityId) || !entityId.StartsWith("climate.", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var newState = data.Value<JObject>("new_state");
            if (newState == null)
            {
                if (_knownThermostats.TryRemove(entityId, out _))
                {
                    ThermostatRemoved?.Invoke(this, new ThermostatRemovedEventArgs(entityId));
                }

                return;
            }

            var parsedState = ParseClimateState(newState);
            if (parsedState == null)
            {
                return;
            }

            var isNew = !_knownThermostats.ContainsKey(entityId);
            _knownThermostats[entityId] = parsedState;

            if (isNew)
            {
                ThermostatDiscovered?.Invoke(this, new ThermostatDiscoveredEventArgs(entityId, parsedState.FriendlyName, parsedState.Model, parsedState));
            }
            else
            {
                ThermostatUpdated?.Invoke(this, new ThermostatUpdatedEventArgs(entityId, parsedState));
            }
        }

        private void ProcessInitialStates(JArray array)
        {
            foreach (var token in array.OfType<JObject>())
            {
                var state = ParseClimateState(token);
                if (state == null)
                {
                    continue;
                }

                if (_knownThermostats.TryAdd(state.EntityId, state))
                {
                    ThermostatDiscovered?.Invoke(this, new ThermostatDiscoveredEventArgs(state.EntityId, state.FriendlyName, state.Model, state));
                }
                else
                {
                    ThermostatUpdated?.Invoke(this, new ThermostatUpdatedEventArgs(state.EntityId, state));
                }
            }
        }

        private ClimateDeviceState ParseClimateState(JObject state)
        {
            var entityId = state.Value<string>("entity_id");
            if (string.IsNullOrEmpty(entityId) || !entityId.StartsWith("climate.", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var attributes = state.Value<JObject>("attributes") ?? new JObject();
            var friendlyName = attributes.Value<string>("friendly_name") ?? entityId;
            var model = attributes.Value<string>("model") ?? "HA Climate";
            var hvacModes = attributes.Value<JArray>("hvac_modes")?.Select(x => x.Value<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();

            var parsed = new ClimateDeviceState
            {
                EntityId = entityId,
                FriendlyName = friendlyName,
                Model = model,
                CurrentTemperature = attributes.Value<double?>("current_temperature"),
                TargetTemperature = attributes.Value<double?>("temperature"),
                TargetTemperatureLow = attributes.Value<double?>("target_temp_low"),
                TargetTemperatureHigh = attributes.Value<double?>("target_temp_high"),
                HvacMode = state.Value<string>("state"),
                HvacModes = hvacModes,
                Action = attributes.Value<string>("hvac_action") ?? attributes.Value<string>("action"),
                Humidity = attributes.Value<double?>("current_humidity"),
                LastUpdated = state.Value<DateTimeOffset?>("last_updated") ?? DateTimeOffset.UtcNow
            };

            return parsed;
        }

        private async Task<JObject> ReceiveMessageAsync(CancellationToken cancellationToken)
        {
            var buffer = new ArraySegment<byte>(new byte[8192]);
            using (var ms = new MemoryStream())
            {
                while (true)
                {
                    var result = await _client.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken).ConfigureAwait(false);
                        return null;
                    }

                    ms.Write(buffer.Array, buffer.Offset, result.Count);

                    if (result.EndOfMessage)
                    {
                        break;
                    }
                }

                var json = Encoding.UTF8.GetString(ms.ToArray());
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return JObject.Parse(json);
            }
        }

        private async Task SendAsync(JObject message, CancellationToken cancellationToken)
        {
            var payload = Encoding.UTF8.GetBytes(message.ToString());
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _client.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task<JToken> SendRequestAsync(JObject message, CancellationToken cancellationToken)
        {
            var id = message.Value<int>("id");
            var tcs = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingRequests.TryAdd(id, tcs))
            {
                throw new InvalidOperationException("Duplicate message id");
            }

            try
            {
                await SendAsync(message, cancellationToken).ConfigureAwait(false);
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken))
                {
                    using (linked.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false))
                    {
                        return await tcs.Task.ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _pendingRequests.TryRemove(id, out _);
            }
        }

        private async Task SendServiceCommandAsync(string entityId, string service, IDictionary<string, JToken> serviceData, CancellationToken cancellationToken)
        {
            var id = GetMessageId();
            var message = HomeAssistantMessageFactory.CreateCallServiceMessage(id, entityId, service, serviceData);
            await SendAsync(message, cancellationToken).ConfigureAwait(false);
        }

        private int GetMessageId()
        {
            return Interlocked.Increment(ref _messageId);
        }
    }
}
