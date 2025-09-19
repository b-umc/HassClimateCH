using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK.EntityModel;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;
using System;

public class Light : ReflectedAttributeDriverEntity
{
    private readonly Action<string, bool> _setLightState;

    public Light(string controllerId, Action<string, bool> setLightState) : base(controllerId)
    {
        _setLightState = setLightState;
    }

    [EntityProperty(Id = "light:isOn")]
    public bool LightIsOn { get; private set; }

    [EntityCommand(Id = "light:on")]
    public void LightOn()
    {
        SetLightOn(true);
    }

    [EntityCommand(Id = "light:off")]
    public void LightOff()
    {
        SetLightOn(false);
    }

    private void SetLightOn(bool on)
    {
        // Here we just update feedback immediately in response to the command
        // This can also be done whenever feedback indicates the state of the light changed
        if (LightIsOn != on)
        {
            LightIsOn = on;
            NotifyPropertyChanged("light:isOn", new DriverEntityValue(on));
        }

        _setLightState(ControllerId, on);
    }
}