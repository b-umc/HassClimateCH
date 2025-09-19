using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK.EntityModel;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;
using HomeAssistantClimate.Gateway;

namespace HomeAssistantClimate.Devices
{
    public sealed class HomeAssistantClimateDevice : ReflectedAttributeDriverEntity, IDisposable
    {
        private readonly string _friendlyName;
        private readonly HomeAssistantGateway _gateway;
        private readonly object _syncRoot = new object();

        private double? _currentTemp;
        private double? _targetTemp;
        private double? _targetLow;
        private double? _targetHigh;
        private double? _humidity;
        private string _hvacMode = "off";
        private string _action = string.Empty;
        private IReadOnlyList<string> _availableModes = Array.Empty<string>();
        private bool _isConnected;

        public HomeAssistantClimateDevice(string entityId, string friendlyName, HomeAssistantGateway gateway)
            : base(entityId)
        {
            _friendlyName = friendlyName;
            _gateway = gateway;
        }

        [EntityProperty(Id = "climate:friendlyName")]
        public string FriendlyName => _friendlyName;

        [EntityProperty(Id = "climate:isConnected")]
        public bool IsConnected => _isConnected;

        [EntityProperty(Id = "climate:currentTemperature")]
        public double? CurrentTemperature => _currentTemp;

        [EntityProperty(Id = "climate:targetTemperature")]
        public double? TargetTemperature => _targetTemp;

        [EntityProperty(Id = "climate:targetTempLow")]
        public double? TargetTemperatureLow => _targetLow;

        [EntityProperty(Id = "climate:targetTempHigh")]
        public double? TargetTemperatureHigh => _targetHigh;

        [EntityProperty(Id = "climate:humidity")]
        public double? Humidity => _humidity;

        [EntityProperty(Id = "climate:hvacMode")]
        public string HvacMode => _hvacMode;

        [EntityProperty(Id = "climate:hvacAction")]
        public string Action => _action;

        [EntityProperty(Id = "climate:availableHvacModes")]
        public string AvailableHvacModes => string.Join(", ", _availableModes ?? Array.Empty<string>());

        [EntityCommand(Id = "climate:setTargetTemperature")]
        public void SetTargetTemperature(DriverEntityValue? value)
        {
            if (!TryGetDouble(value, out var newTemp))
            {
                return;
            }

            UpdateProperty(ref _targetTemp, newTemp, "climate:targetTemperature");
            _ = Task.Run(() => _gateway.SetTargetTemperatureAsync(ControllerId, newTemp, null, null, CancellationToken.None));
        }

        [EntityCommand(Id = "climate:setTargetTempLow")]
        public void SetTargetTempLow(DriverEntityValue? value)
        {
            if (!TryGetDouble(value, out var low))
            {
                return;
            }

            UpdateProperty(ref _targetLow, low, "climate:targetTempLow");
            _ = Task.Run(() => _gateway.SetTargetTemperatureAsync(ControllerId, null, _targetLow, _targetHigh, CancellationToken.None));
        }

        [EntityCommand(Id = "climate:setTargetTempHigh")]
        public void SetTargetTempHigh(DriverEntityValue? value)
        {
            if (!TryGetDouble(value, out var high))
            {
                return;
            }

            UpdateProperty(ref _targetHigh, high, "climate:targetTempHigh");
            _ = Task.Run(() => _gateway.SetTargetTemperatureAsync(ControllerId, null, _targetLow, _targetHigh, CancellationToken.None));
        }

        [EntityCommand(Id = "climate:setHvacMode")]
        public void SetHvacMode(DriverEntityValue? modeValue)
        {
            var mode = TryGetString(modeValue);
            if (mode == null)
            {
                return;
            }

            UpdateProperty(ref _hvacMode, mode, "climate:hvacMode");
            _ = Task.Run(() => _gateway.SetHvacModeAsync(ControllerId, mode, CancellationToken.None));
        }

        [EntityCommand(Id = "climate:setPresetMode")]
        public void SetPresetMode(DriverEntityValue? presetValue)
        {
            var preset = TryGetString(presetValue);
            if (preset == null)
            {
                return;
            }

            _ = Task.Run(() => _gateway.SetPresetModeAsync(ControllerId, preset, CancellationToken.None));
        }

        [EntityCommand(Id = "climate:setFanMode")]
        public void SetFanMode(DriverEntityValue? modeValue)
        {
            var mode = TryGetString(modeValue);
            if (mode == null)
            {
                return;
            }

            _ = Task.Run(() => _gateway.SetFanModeAsync(ControllerId, mode, CancellationToken.None));
        }

        [EntityCommand(Id = "climate:increaseSetpoint")]
        public void IncreaseSetpoint()
        {
            var newTarget = (_targetTemp ?? _currentTemp ?? 70.0) + 0.5;
            UpdateProperty(ref _targetTemp, newTarget, "climate:targetTemperature");
            _ = Task.Run(() => _gateway.SetTargetTemperatureAsync(ControllerId, newTarget, null, null, CancellationToken.None));
        }

        [EntityCommand(Id = "climate:decreaseSetpoint")]
        public void DecreaseSetpoint()
        {
            var newTarget = (_targetTemp ?? _currentTemp ?? 70.0) - 0.5;
            UpdateProperty(ref _targetTemp, newTarget, "climate:targetTemperature");
            _ = Task.Run(() => _gateway.SetTargetTemperatureAsync(ControllerId, newTarget, null, null, CancellationToken.None));
        }

        public void SetConnectionState(bool connected)
        {
            UpdateProperty(ref _isConnected, connected, "climate:isConnected");
        }

        public void UpdateFromState(ClimateDeviceState state)
        {
            if (state == null)
            {
                return;
            }

            UpdateProperty(ref _currentTemp, state.CurrentTemperature, "climate:currentTemperature");
            UpdateProperty(ref _targetTemp, state.TargetTemperature, "climate:targetTemperature");
            UpdateProperty(ref _targetLow, state.TargetTemperatureLow, "climate:targetTempLow");
            UpdateProperty(ref _targetHigh, state.TargetTemperatureHigh, "climate:targetTempHigh");
            UpdateProperty(ref _humidity, state.Humidity, "climate:humidity");
            UpdateProperty(ref _hvacMode, state.HvacMode, "climate:hvacMode");
            UpdateProperty(ref _action, state.Action, "climate:hvacAction");
            UpdateModes(state.HvacModes);
        }

        private void UpdateModes(IReadOnlyList<string> modes)
        {
            lock (_syncRoot)
            {
                if (modes == null)
                {
                    modes = Array.Empty<string>();
                }

                if (_availableModes == null || !AreModesEqual(_availableModes, modes))
                {
                    _availableModes = modes;
                    NotifyPropertyChanged("climate:availableHvacModes", CreateValueForObject(AvailableHvacModes));
                }
            }
        }

        private static bool AreModesEqual(IReadOnlyList<string> first, IReadOnlyList<string> second)
        {
            if (first == null && second == null)
            {
                return true;
            }

            if (first == null || second == null)
            {
                return false;
            }

            if (first.Count != second.Count)
            {
                return false;
            }

            for (var i = 0; i < first.Count; i++)
            {
                if (!string.Equals(first[i], second[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdateProperty<T>(ref T field, T value, string propertyId)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            NotifyPropertyChanged(propertyId, CreateValueForObject(value));
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        private static bool TryGetDouble(DriverEntityValue? value, out double result)
        {
            result = default;

            if (!value.HasValue)
            {
                return false;
            }

            var raw = value.Value.GetValue<object>();
            if (raw == null)
            {
                return false;
            }

            try
            {
                result = Convert.ToDouble(raw);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (InvalidCastException)
            {
                return false;
            }
        }

        private static string TryGetString(DriverEntityValue? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            var raw = value.Value.GetValue<object>();
            return raw?.ToString();
        }
    }
}
