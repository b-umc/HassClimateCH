using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;

[assembly: DriverAssemblyEntryPoint(typeof(HomeAssistantClimate.EntryPoint))]

namespace HomeAssistantClimate
{
    public sealed class EntryPoint : DriverAssemblyEntryPoint
    {
        public override DriverController CreateDriverControllerInstance(DriverControllerCreationArgs args)
        {
            var resources = DriverImplementationResources.FromCreationArgs(args, typeof(EntryPoint));

            var platform = new HomeAssistantPlatform(args, resources);
            var entity = new ConfigurableDriverEntity(platform.ControllerId, platform, platform.ConfigurationController);

            return new DispatchingDeviceController(entity, args, null);
        }
    }
}
