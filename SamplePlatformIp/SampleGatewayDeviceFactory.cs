using System;
using Crestron.SimplSharp;
using PairedExtensionDevice_Crestron_Sample_IP;
using SamplePairedDeviceDriver.Devices;

namespace Crestron.Samples.Platforms
{
	public class SampleGatewayDeviceFactory
	{
		private const uint NoOfDevicesToCreate = 3;
		private const uint DiscoverDeviceTime = 5000;
		private const string DisplayDeviceIdFormat = "SamplePairedDisplay_{0}";
		private const string DisplayDeviceNameFormat = "Projector {0}";
		private const string ExtensionDeviceIdFormat = "SamplePairedExtension_{0}";
		private const string ExtensionDeviceNameFormat = "Extension {0}";

		private readonly CTimer _deviceStateTimer;
		private DeviceFactoryState _deviceFactoryState = DeviceFactoryState.None;

		internal event EventHandler<SampleDeviceFactoryStatusEventArgs> DeviceStatusChanged;

		public SampleGatewayDeviceFactory()
		{
			_deviceStateTimer = new CTimer(DeviceStateTimerCallback, Timeout.Infinite);
		}

		public void DiscoverDevices()
		{
			_deviceFactoryState = DeviceFactoryState.DiscoverDevices;
			_deviceStateTimer.Reset(DiscoverDeviceTime);
		}

		public void Dispose()
		{
			if (_deviceStateTimer != null)
			{
				_deviceStateTimer.Stop();
				_deviceStateTimer.Dispose();
			}
			_deviceFactoryState = DeviceFactoryState.None;
		}

		private void DeviceStateTimerCallback(object userSpecific)
		{
			switch (_deviceFactoryState)
			{
				case DeviceFactoryState.DiscoverDevices:
				{
					// Create paired devices.
					for (int i = 1; i <= NoOfDevicesToCreate; i++)
					{
						// Create projector paired devices
						var pairedDevice = new SamplePairedDevice(string.Format(DisplayDeviceIdFormat, i), string.Format(DisplayDeviceNameFormat, i));

						if (DeviceStatusChanged != null)
							DeviceStatusChanged(this, new SampleDeviceFactoryStatusEventArgs(DeviceStatus.Added, pairedDevice));

						// Create extension paired devices
						var extensionPairedDevice = new SamplePairedExtension(string.Format(ExtensionDeviceIdFormat, i), string.Format(ExtensionDeviceNameFormat, i));

						if (DeviceStatusChanged != null)
							DeviceStatusChanged(this, new SampleDeviceFactoryStatusEventArgs(DeviceStatus.Added, extensionPairedDevice));
					}

					break;
				}
			}
		}

		enum DeviceFactoryState
		{
			None,
			DiscoverDevices,
			Active,
			Inactive
		}
	}

	internal enum DeviceStatus
	{
		Added,
		Removed,
		Updated
	}
}