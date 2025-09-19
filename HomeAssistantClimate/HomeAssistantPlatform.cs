using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK.EntityModel;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;
using Crestron.DeviceDrivers.SDK;
using Definitions;
using HomeAssistantClimate.Devices;
using HomeAssistantClimate.Gateway;

namespace HomeAssistantClimate
{
    public class HomeAssistantPlatform : ReflectedAttributeDriverEntity
    {
        private readonly object _syncRoot = new object();
        private readonly IDictionary<string, HomeAssistantThermostat> _thermostats = new Dictionary<string, HomeAssistantThermostat>();
        private readonly IDictionary<string, PlatformManagedDevice> _managedDevices = new Dictionary<string, PlatformManagedDevice>();

        private HomeAssistantGateway _gateway;
        private CancellationTokenSource _cts;
        private bool _isConnected;

        private string _host = string.Empty;
        private ushort _port = 8123;
        private bool _useSsl;
        private string _token = string.Empty;

        public HomeAssistantPlatform(DriverControllerCreationArgs args, DriverImplementationResources resources)
            : base(DriverController.RootControllerId)
        {
            var cfgArgs = DataDrivenConfigurationControllerArgs.FromResources(args, resources, ControllerId);
            ConfigurationController = new DelegateDataDrivenConfigurationController(cfgArgs, ApplyConfigurationItems, null, null);
        }

        internal DataDrivenConfigurationController ConfigurationController { get; }

        [EntityProperty(
            Id = "platform:managedDevices",
            Type = DriverEntityValueType.DeviceDictionary,
            ItemTypeRef = "platform:ManagedDevice"
        )]
        public IDictionary<string, PlatformManagedDevice> ManagedDevices
        {
            get
            {
                lock (_syncRoot)
                {
                    return new Dictionary<string, PlatformManagedDevice>(_managedDevices);
                }
            }
        }

        private ConfigurationItemErrors ApplyConfigurationItems(
            DataDrivenConfigurationController.ApplyConfigurationAction action,
            string stepId,
            IDictionary<string, DriverEntityValue?> values)
        {
            switch (action)
            {
                case DataDrivenConfigurationController.ApplyConfigurationAction.ApplyAll:
                case DataDrivenConfigurationController.ApplyConfigurationAction.ApplyStep:
                    return ApplyConfiguration(values);
                case DataDrivenConfigurationController.ApplyConfigurationAction.ClearValues:
                    StopGateway();
                    break;
            }

            return null;
        }

        private ConfigurationItemErrors ApplyConfiguration(IDictionary<string, DriverEntityValue?> values)
        {
            if (values == null)
            {
                return new ConfigurationItemErrors(new Dictionary<string, string> { { "_Host_", "Host is required" } }, null);
            }

            var errors = new Dictionary<string, string>();

            if (values.TryGetValue("_Host_", out var hostValue) && hostValue.HasValue)
            {
                _host = hostValue.Value.GetValue<string>();
            }
            else if (string.IsNullOrWhiteSpace(_host))
            {
                errors["_Host_"] = "Host is required";
            }

            if (values.TryGetValue("_Port_", out var portValue) && portValue.HasValue)
            {
                var parsedPort = (ushort)portValue.Value.GetValue<long>();
                if (parsedPort == 0)
                {
                    errors["_Port_"] = "Port must be greater than 0";
                }
                else
                {
                    _port = parsedPort;
                }
            }

            if (values.TryGetValue("UseSsl", out var sslValue) && sslValue.HasValue)
            {
                _useSsl = sslValue.Value.GetValue<bool>();
            }

            if (values.TryGetValue("AccessToken", out var tokenValue) && tokenValue.HasValue)
            {
                _token = tokenValue.Value.GetValue<string>();
            }

            if (string.IsNullOrWhiteSpace(_token))
            {
                errors["AccessToken"] = "Long lived access token is required";
            }

            if (errors.Any())
            {
                return new ConfigurationItemErrors(errors, null);
            }

            StartGateway();
            return null;
        }

        private void StartGateway()
        {
            StopGateway();

            if (string.IsNullOrWhiteSpace(_host) || string.IsNullOrWhiteSpace(_token))
            {
                return;
            }

            var uriBuilder = new UriBuilder
            {
                Host = _host,
                Scheme = _useSsl ? "wss" : "ws",
                Port = _port,
                Path = "/api/websocket"
            };

            _cts = new CancellationTokenSource();

            _gateway = new HomeAssistantGateway(uriBuilder.Uri, _token);
            _gateway.ThermostatDiscovered += GatewayOnThermostatDiscovered;
            _gateway.ThermostatRemoved += GatewayOnThermostatRemoved;
            _gateway.ThermostatUpdated += GatewayOnThermostatUpdated;
            _gateway.ConnectionStateChanged += GatewayOnConnectionStateChanged;

            Task.Run(() => _gateway.StartAsync(_cts.Token));
        }

        private void StopGateway()
        {
            _isConnected = false;

            if (_gateway != null)
            {
                _gateway.ThermostatDiscovered -= GatewayOnThermostatDiscovered;
                _gateway.ThermostatRemoved -= GatewayOnThermostatRemoved;
                _gateway.ThermostatUpdated -= GatewayOnThermostatUpdated;
                _gateway.ConnectionStateChanged -= GatewayOnConnectionStateChanged;
                _gateway.Dispose();
                _gateway = null;
            }

            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            lock (_syncRoot)
            {
                var controllerIds = new List<string>(_thermostats.Keys);

                foreach (var id in controllerIds)
                {
                    RemoveSubController(id);
                }

                foreach (var thermostat in _thermostats.Values)
                {
                    thermostat.Dispose();
                }

                _thermostats.Clear();
                _managedDevices.Clear();

                NotifyPropertyChanged("platform:managedDevices", CreateValueForEntries(_managedDevices));
            }
        }

        private void GatewayOnConnectionStateChanged(object sender, bool e)
        {
            _isConnected = e;

            lock (_syncRoot)
            {
                foreach (var thermostat in _thermostats.Values)
                {
                    thermostat.SetConnectionState(e);
                }
            }
        }

        private void GatewayOnThermostatDiscovered(object sender, ThermostatDiscoveredEventArgs e)
        {
            lock (_syncRoot)
            {
                if (_thermostats.TryGetValue(e.EntityId, out var existing))
                {
                    existing.SetConnectionState(_isConnected);
                    existing.UpdateFromState(e.State);
                    return;
                }

                var thermostat = new HomeAssistantThermostat(e.EntityId, e.FriendlyName, _gateway);
                thermostat.SetConnectionState(_isConnected);
                thermostat.UpdateFromState(e.State);

                _thermostats[e.EntityId] = thermostat;

                var managedDevice = new PlatformManagedDevice(DeviceUxCategory.Hvac, e.FriendlyName, "Home Assistant", e.Model, e.EntityId);
                _managedDevices[e.EntityId] = managedDevice;

                var subControllers = new[]
                {
                    new ConfigurableDriverEntity(thermostat.ControllerId, thermostat, null)
                };

                UpdateSubControllers(subControllers, null);

                NotifyPropertyChanged("platform:managedDevices", CreateValueForEntries(_managedDevices));
            }
        }

        private void GatewayOnThermostatUpdated(object sender, ThermostatUpdatedEventArgs e)
        {
            lock (_syncRoot)
            {
                if (_thermostats.TryGetValue(e.EntityId, out var thermostat))
                {
                    thermostat.UpdateFromState(e.State);
                }
            }
        }

        private void GatewayOnThermostatRemoved(object sender, ThermostatRemovedEventArgs e)
        {
            lock (_syncRoot)
            {
                if (_thermostats.Remove(e.EntityId, out var thermostat))
                {
                    thermostat.Dispose();
                    RemoveSubController(thermostat.ControllerId);
                }

                if (_managedDevices.Remove(e.EntityId))
                {
                    NotifyPropertyChanged("platform:managedDevices", CreateValueForEntries(_managedDevices));
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopGateway();
            }

            base.Dispose(disposing);
        }
    }
}
