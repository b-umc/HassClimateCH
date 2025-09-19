using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK.EntityModel;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;
using HomeAssistantClimate.Gateway;

namespace HomeAssistantClimate.Devices
{
    public sealed class HomeAssistantThermostat : ReflectedAttributeDriverEntity, IDisposable
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

        public HomeAssistantThermostat(string entityId, string friendlyName, HomeAssistantGateway gateway)
            : base(entityId)
        {
            _friendlyName = friendlyName;
            _gateway = gateway;
        }

        [EntityProperty(Id = "thermostat:friendlyName", Type = DriverEntityValueType.String)]
        public string FriendlyName => _friendlyName;

        [EntityProperty(Id = "thermostat:isConnected", Type = DriverEntityValueType.Bool)]
        public bool IsConnected => _isConnected;

        [EntityProperty(Id = "thermostat:currentTemperature", Type = DriverEntityValueType.Double)]
        public double? CurrentTemperature => _currentTemp;

        [EntityProperty(Id = "thermostat:targetTemperature", Type = DriverEntityValueType.Double)]
        public double? TargetTemperature => _targetTemp;

        [EntityProperty(Id = "thermostat:targetTempLow", Type = DriverEntityValueType.Double)]
        public double? TargetTemperatureLow => _targetLow;

        [EntityProperty(Id = "thermostat:targetTempHigh", Type = DriverEntityValueType.Double)]
        public double? TargetTemperatureHigh => _targetHigh;

        [EntityProperty(Id = "thermostat:humidity", Type = DriverEntityValueType.Double)]
        public double? Humidity => _humidity;

        [EntityProperty(Id = "thermostat:hvacMode", Type = DriverEntityValueType.String)]
        public string HvacMode => _hvacMode;

        [EntityProperty(Id = "thermostat:hvacAction", Type = DriverEntityValueType.String)]
        public string Action => _action;

        [EntityProperty(Id = "thermostat:availableHvacModes", Type = DriverEntityValueType.String)]
        public string AvailableHvacModes => string.Join(", ", _availableModes ?? Array.Empty<string>());

        [EntityCommand(Id = "thermostat:setTargetTemperature")]
        public void SetTargetTemperature(DriverEntityValue value)
        {
            if (value == null || !value.HasValue)
            {
                return;
            }

            var newTemp = Convert.ToDouble(value.GetValue<object>());
            _ = Task.Run(() => _gateway.SetTargetTemperatureAsync(ControllerId, newTemp, null, null, CancellationToken.None));
        }

        [EntityCommand(Id = "thermostat:setTargetTempLow")]
        public void SetTargetTempLow(DriverEntityValue value)
        {
            if (value == null || !value.HasValue)
            {
                return;
            }

            var low = Convert.ToDouble(value.GetValue<object>());
            _ = Task.Run(() => _gateway.SetTargetTemperatureAsync(ControllerId, null, low, _targetHigh, CancellationToken.None));
        }

        [EntityCommand(Id = "thermostat:setTargetTempHigh")]
        public void SetTargetTempHigh(DriverEntityValue value)
        {
            if (value == null || !value.HasValue)
            {
                return;
            }

            var high = Convert.ToDouble(value.GetValue<object>());
            _ = Task.Run(() => _gateway.SetTargetTemperatureAsync(ControllerId, null, _targetLow, high, CancellationToken.None));
        }

        [EntityCommand(Id = "thermostat:setHvacMode")]
        public void SetHvacMode(DriverEntityValue modeValue)
        {
            if (modeValue == null || !modeValue.HasValue)
            {
                return;
            }

            var mode = Convert.ToString(modeValue.GetValue<object>());
            _ = Task.Run(() => _gateway.SetHvacModeAsync(ControllerId, mode, CancellationToken.None));
        }

        [EntityCommand(Id = "thermostat:setPresetMode")]
        public void SetPresetMode(DriverEntityValue presetValue)
        {
            if (presetValue == null || !presetValue.HasValue)
            {
                return;
            }

            var preset = Convert.ToString(presetValue.GetValue<object>());
            _ = Task.Run(() => _gateway.SetPresetModeAsync(ControllerId, preset, CancellationToken.None));
        }

        [EntityCommand(Id = "thermostat:setFanMode")]
        public void SetFanMode(DriverEntityValue modeValue)
        {
            if (modeValue == null || !modeValue.HasValue)
            {
                return;
            }

            var mode = Convert.ToString(modeValue.GetValue<object>());
            _ = Task.Run(() => _gateway.SetFanModeAsync(ControllerId, mode, CancellationToken.None));
        }

        [EntityCommand(Id = "thermostat:increaseSetpoint")]
        public void IncreaseSetpoint()
        {
            var newTarget = (_targetTemp ?? _currentTemp ?? 70.0) + 0.5;
            _ = Task.Run(() => _gateway.SetTargetTemperatureAsync(ControllerId, newTarget, null, null, CancellationToken.None));
        }

        [EntityCommand(Id = "thermostat:decreaseSetpoint")]
        public void DecreaseSetpoint()
        {
            var newTarget = (_targetTemp ?? _currentTemp ?? 70.0) - 0.5;
            _ = Task.Run(() => _gateway.SetTargetTemperatureAsync(ControllerId, newTarget, null, null, CancellationToken.None));
        }

        public void SetConnectionState(bool connected)
        {
            UpdateProperty(ref _isConnected, connected, "thermostat:isConnected");
        }

        public void UpdateFromState(ClimateDeviceState state)
        {
            if (state == null)
            {
                return;
            }

            UpdateProperty(ref _currentTemp, state.CurrentTemperature, "thermostat:currentTemperature");
            UpdateProperty(ref _targetTemp, state.TargetTemperature, "thermostat:targetTemperature");
            UpdateProperty(ref _targetLow, state.TargetTemperatureLow, "thermostat:targetTempLow");
            UpdateProperty(ref _targetHigh, state.TargetTemperatureHigh, "thermostat:targetTempHigh");
            UpdateProperty(ref _humidity, state.Humidity, "thermostat:humidity");
            UpdateProperty(ref _hvacMode, state.HvacMode, "thermostat:hvacMode");
            UpdateProperty(ref _action, state.Action, "thermostat:hvacAction");
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
                    NotifyPropertyChanged("thermostat:availableHvacModes", CreateValueForObject(AvailableHvacModes));
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

        public void Dispose()
        {
        }
    }
}
