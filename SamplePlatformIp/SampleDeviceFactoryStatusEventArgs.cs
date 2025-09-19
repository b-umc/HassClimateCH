using System;
using SamplePlatformCommon;

namespace Crestron.Samples.Platforms
{
	internal class SampleDeviceFactoryStatusEventArgs : EventArgs
	{
		public DeviceStatus Status { get; private set; }

		public IPairedDevice Device { get; private set; }

		public SampleDeviceFactoryStatusEventArgs(DeviceStatus status, IPairedDevice device)
		{
			Status = status;
			Device = device;
		}
	}
}