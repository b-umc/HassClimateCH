using System;
using System.Collections.Generic;

namespace HomeAssistantClimate.Gateway
{
    public class ClimateDeviceState
    {
        public string EntityId { get; set; }
        public string FriendlyName { get; set; }
        public string Model { get; set; }
        public double? CurrentTemperature { get; set; }
        public double? TargetTemperature { get; set; }
        public double? TargetTemperatureLow { get; set; }
        public double? TargetTemperatureHigh { get; set; }
        public string HvacMode { get; set; }
        public IReadOnlyList<string> HvacModes { get; set; }
        public string Action { get; set; }
        public double? Humidity { get; set; }
        public DateTimeOffset LastUpdated { get; set; }
    }

    public sealed class ThermostatDiscoveredEventArgs : EventArgs
    {
        public ThermostatDiscoveredEventArgs(string entityId, string friendlyName, string model, ClimateDeviceState state)
        {
            EntityId = entityId;
            FriendlyName = friendlyName;
            Model = model;
            State = state;
        }

        public string EntityId { get; }
        public string FriendlyName { get; }
        public string Model { get; }
        public ClimateDeviceState State { get; }
    }

    public sealed class ThermostatUpdatedEventArgs : EventArgs
    {
        public ThermostatUpdatedEventArgs(string entityId, ClimateDeviceState state)
        {
            EntityId = entityId;
            State = state;
        }

        public string EntityId { get; }
        public ClimateDeviceState State { get; }
    }

    public sealed class ThermostatRemovedEventArgs : EventArgs
    {
        public ThermostatRemovedEventArgs(string entityId)
        {
            EntityId = entityId;
        }

        public string EntityId { get; }
    }
}
