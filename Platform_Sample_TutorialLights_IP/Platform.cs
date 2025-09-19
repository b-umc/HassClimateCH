using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;
using Definitions;
using System.Collections.Generic;

public class Platform : ReflectedAttributeDriverEntity
{
    private bool _initialized;

    public Platform(DriverControllerCreationArgs args, DriverImplementationResources resources) : base(DriverController
        .RootControllerId)
    {
        DataDrivenConfigurationControllerArgs cfgArgs =
            DataDrivenConfigurationControllerArgs.FromResources(args, resources, ControllerId);
        ConfigurationController = new DelegateDataDrivenConfigurationController(cfgArgs, ApplyConfigurationItems, null, null);
    }

    internal DataDrivenConfigurationController ConfigurationController { get; private set; }

    [EntityProperty(
        Id = "platform:managedDevices",
        Type = DriverEntityValueType.DeviceDictionary,
        ItemTypeRef = "platform:ManagedDevice"
    )]
    public IDictionary<string, PlatformManagedDevice> ManagedDevices { get; private set; }

    private ConfigurationItemErrors ApplyConfigurationItems(
        DataDrivenConfigurationController.ApplyConfigurationAction action,
        string stepId,
        IDictionary<string, DriverEntityValue?> values
    )
    {
        switch (action)
        {
            // In this case, we only have one step, so these are the same
            case DataDrivenConfigurationController.ApplyConfigurationAction.ApplyAll:
            case DataDrivenConfigurationController.ApplyConfigurationAction.ApplyStep:
                DriverEntityValue? value;
                string host;
                ushort port;

                // For initial configuration, host and port should always be specified or this should
                // return an error.
                // After initial configuration, if the user changes only one setting, then this will be
                // called with just the one new setting.
                bool hasHost = values.TryGetValue("_Host_", out value) && value.HasValue;
                if (hasHost)
                {
                    host = value.Value.GetValue<string>();
                }

                bool hasPort = values.TryGetValue("_Port_", out value) && value.HasValue;
                if (hasPort)
                {
                    port = (ushort)value.Value.GetValue<long>();
                }

                // TODO: Validate host, create transport
                // If host is invalid...
                // return new ConfigurationItemErrors(new Dictionary<string, string> { { "_Host_", "Invalid host name" } }, null);

                EmulateInitialization();
                return null;

            case DataDrivenConfigurationController.ApplyConfigurationAction.ClearValues:
                // This happens when the user goes "back" to a previous step during configuration.
                // In our use case here, this is impossible since there's only one step.
                // This code is just provided for reference.
                // The dictionary of values will have items to clear.
                if (values.ContainsKey("_Host_"))
                {
                    // Destroy transport, await new values
                }

                break;
        }

        return null;
    }

    private void EmulateInitialization()
    {
        if (!_initialized)
        {
            _initialized = true;

            var controllersToAdd = new List<ConfigurableDriverEntity>();

            // Create light class
            var light1Entity = new Light("light1", SetLightState);

            // Register this controller so that it can be interacted with by the application using its controller Id
            // In this example, the light will not have configuration.
            controllersToAdd.Add(new ConfigurableDriverEntity(light1Entity.ControllerId, light1Entity, null));

            // Create data structure to describe the light to the application
            var light1 = new PlatformManagedDevice(
                DeviceUxCategory.Light, // UxCategory
                "My First Light", // Name
                "SomeManufacturer", // Manufacturer
                "SomeLightModel", // Model
                "1234" // Serial number (can be null if unknown)
                );

            // Make another light
            var light2Entity = new Light("light2", SetLightState);
            controllersToAdd.Add(new ConfigurableDriverEntity(light2Entity.ControllerId, light2Entity, null));
            var light2 = new PlatformManagedDevice(DeviceUxCategory.Light, "My Second Light", "SomeManufacturer", "SomeLightModel", "5678");

            // Make a room with light control
            var roomWithLightsEntity = new Light("room1", SetLightState);
            controllersToAdd.Add(new ConfigurableDriverEntity(roomWithLightsEntity.ControllerId, roomWithLightsEntity, null));
            var roomWithLights = new PlatformManagedDevice(DeviceUxCategory.Room, "A Room with Lights", "SomeManufacturer", "Room", null);

            // Add controllers all at once to have the update go out in one event
            UpdateSubControllers(controllersToAdd, null);

            // Create the collection of managed devices
            ManagedDevices = new Dictionary<string, PlatformManagedDevice>
            {
                { light1Entity.ControllerId, light1 },
                { light2Entity.ControllerId, light2 },
                { roomWithLightsEntity.ControllerId, roomWithLights }
            };

            // Here we are using the "new value" overload of NotifyPropertyChanged.
            // If only a single device is added or removed, or if only the name changes,
            // the overload using DriverEntityValueUpdate is more efficient.
            NotifyPropertyChanged("platform:managedDevices", CreateValueForEntries(ManagedDevices));
        }
    }

    private void EmulateNameChange()
    {
        // Pretend "My Second Light" -> "Second Light"
        var light2 = new PlatformManagedDevice(DeviceUxCategory.Light, "Second Light", "SomeManufacturer", "SomeLightModel", "5678");

        // Since we don't have a lock here around access to ManagedDevices, we must
        // assign a copy to prevent a concurrent modification exception if the application
        // is currently reading the value
        var copy = new Dictionary<string, PlatformManagedDevice>(ManagedDevices);
        copy["light2"] = light2;
        ManagedDevices = copy;

        // Create property change event for the name property of light2
        var nameChange = DriverEntityValueUpdate.Create("name", new DriverEntityValue("Second Light"));

        // Create the collection of changes for managed devices
        // The nesting is because if both light1 and light2 changed, we would need to list
        // both changes under managed devices. Here, our change list is 1 item long.
        var managedDevicesChange = DriverEntityValueUpdate.Create(
            DriverEntityValueUpdate.Create("light2", nameChange)
        );

        NotifyPropertyChanged("platform:managedDevices", managedDevicesChange);
    }

    private void EmulateAddition()
    {
        // Make another light
        var light3Instance = new Light("light3", SetLightState);
        var light3Entity = new ConfigurableDriverEntity(light3Instance.ControllerId, light3Instance, null);
        UpdateSubControllers(new ConfigurableDriverEntity[] { light3Entity }, null);
        var light3 = new PlatformManagedDevice(DeviceUxCategory.Light, "My Third Light", "SomeManufacturer", "SomeLightModel", "9012");

        // Since we don't have a lock here around access to ManagedDevices, we must
        // assign a copy to prevent a concurrent modification exception if the application
        // is currently reading the value
        var copy = new Dictionary<string, PlatformManagedDevice>(ManagedDevices) { { light3Instance.ControllerId, light3 } };
        ManagedDevices = copy;

        // Notify the application of the addition to the collection
        // Is nested since the key is inside the managed devices dictionary
        // and we could potentially be updating more than one.
        var managedDevicesChange = DriverEntityValueUpdate.Create(
            DriverEntityValueUpdate.Create(light3Instance.ControllerId, CreateValueForObject(light3))
        );

        NotifyPropertyChanged("platform:managedDevices", managedDevicesChange);
    }

    private void EmulateRemoval()
    {
        string controllerToRemove = "light1";

        // Since we don't have a lock here around access to ManagedDevices, we must
        // assign a copy to prevent a concurrent modification exception if the application
        // is currently reading the value
        var copy = new Dictionary<string, PlatformManagedDevice>(ManagedDevices);
        copy.Remove(controllerToRemove);
        ManagedDevices = copy;

        // Tell the application about the change
        // Is nested since the key is inside the managed devices dictionary
        // and we could potentially be updating more than one.
        var managedDevicesChange = DriverEntityValueUpdate.Create(
            DriverEntityValueUpdate.CreateDeletion(controllerToRemove)
        );

        NotifyPropertyChanged("platform:managedDevices", managedDevicesChange);

        // Remove controller now that it's gone from our managed device list
        UpdateSubControllers(null, new string[] { controllerToRemove });
    }

    private void SetLightState(string lightId, bool on)
    {
        // Would send command to device here
    }
}