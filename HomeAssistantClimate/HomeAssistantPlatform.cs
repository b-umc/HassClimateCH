using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crestron.DeviceDrivers.EntityModel;
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
        private readonly IDictionary<string, HomeAssistantClimateDevice> _climateDevices = new Dictionary<string, HomeAssistantClimateDevice>();
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
            _gateway.ClimateDeviceDiscovered += GatewayOnClimateDeviceDiscovered;
            _gateway.ClimateDeviceRemoved += GatewayOnClimateDeviceRemoved;
            _gateway.ClimateDeviceUpdated += GatewayOnClimateDeviceUpdated;
            _gateway.ConnectionStateChanged += GatewayOnConnectionStateChanged;

            Task.Run(() => _gateway.StartAsync(_cts.Token));
        }

        private void StopGateway()
        {
            _isConnected = false;

            if (_gateway != null)
            {
                _gateway.ClimateDeviceDiscovered -= GatewayOnClimateDeviceDiscovered;
                _gateway.ClimateDeviceRemoved -= GatewayOnClimateDeviceRemoved;
                _gateway.ClimateDeviceUpdated -= GatewayOnClimateDeviceUpdated;
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
                var controllerIds = _climateDevices.Keys.ToArray();
                if (controllerIds.Length > 0)
                {
                    UpdateSubControllers(null, controllerIds);
                }

                foreach (var device in _climateDevices.Values)
                {
                    device.Dispose();
                }

                _climateDevices.Clear();
                _managedDevices.Clear();

                NotifyPropertyChanged("platform:managedDevices", CreateValueForEntries(_managedDevices));
            }
        }

        private void GatewayOnConnectionStateChanged(object sender, bool e)
        {
            _isConnected = e;

            lock (_syncRoot)
            {
                foreach (var device in _climateDevices.Values)
                {
                    device.SetConnectionState(e);
                }
            }
        }

        private void GatewayOnClimateDeviceDiscovered(object sender, ClimateDeviceDiscoveredEventArgs e)
        {
            lock (_syncRoot)
            {
                if (_climateDevices.TryGetValue(e.EntityId, out var existing))
                {
                    existing.SetConnectionState(_isConnected);
                    existing.UpdateFromState(e.State);
                    return;
                }

                var device = new HomeAssistantClimateDevice(e.EntityId, e.FriendlyName, _gateway);
                device.SetConnectionState(_isConnected);
                device.UpdateFromState(e.State);

                _climateDevices[e.EntityId] = device;

                var managedDevice = new PlatformManagedDevice(DeviceUxCategory.Hvac, e.FriendlyName, "Home Assistant", e.Model, e.EntityId);
                _managedDevices[e.EntityId] = managedDevice;

                var subControllers = new[]
                {
                    new ConfigurableDriverEntity(device.ControllerId, device, null)
                };

                UpdateSubControllers(subControllers, null);

                NotifyPropertyChanged("platform:managedDevices", CreateValueForEntries(_managedDevices));
            }
        }

        private void GatewayOnClimateDeviceUpdated(object sender, ClimateDeviceUpdatedEventArgs e)
        {
            lock (_syncRoot)
            {
                if (_climateDevices.TryGetValue(e.EntityId, out var device))
                {
                    device.UpdateFromState(e.State);
                }
            }
        }

        private void GatewayOnClimateDeviceRemoved(object sender, ClimateDeviceRemovedEventArgs e)
        {
            lock (_syncRoot)
            {
                if (_climateDevices.Remove(e.EntityId, out var device))
                {
                    device.Dispose();
                    UpdateSubControllers(null, new[] { device.ControllerId });
                }

                if (_managedDevices.Remove(e.EntityId))
                {
                    NotifyPropertyChanged("platform:managedDevices", CreateValueForEntries(_managedDevices));
                }
            }
        }

        public override void Dispose()
        {
            StopGateway();
            base.Dispose();
        }
    }
}
