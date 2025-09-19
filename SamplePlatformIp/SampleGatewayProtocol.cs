using System;
using System.Collections.Generic;
using Crestron.RAD.Common.BasicDriver;
using Crestron.RAD.Common.Enums;
using Crestron.RAD.Common.Transports;
using Crestron.RAD.DeviceTypes.Gateway;
using Crestron.SimplSharp;
using SamplePlatformCommon;

namespace Crestron.Samples.Platforms
{
	public class SampleGatewayProtocol : AGatewayProtocol
	{
		#region Fields

		private readonly Dictionary<string, IPairedDevice> _pairedDevices = new Dictionary<string, IPairedDevice>();
		private readonly SampleGatewayDeviceFactory _deviceFactory;

		private CCriticalSection _pairedDevicesLock = new CCriticalSection();

		#endregion

		#region Initialization

		public SampleGatewayProtocol(ISerialTransport transport, byte id) : base(transport, id)
		{
			ValidateResponse = GatewayValidateResponse;

			_deviceFactory = new SampleGatewayDeviceFactory();
		}

		#endregion

		#region Base Members

		protected override void ChooseDeconstructMethod(ValidatedRxData validatedData)
		{
			// This is responsible for parsing the response from device and take apropriate updates to driver and notify applications about the change.
			if (validatedData.Data == "DiscoverDevicesResponse")
			{
				_deviceFactory.DiscoverDevices();
			}
		}

		protected override void ConnectionChangedEvent(bool connection)
		{
			base.ConnectionChangedEvent(connection);

			foreach (var samplePairedDevice in _pairedDevices.Values)
				samplePairedDevice.SetConnectionStatus(connection);
		}

		#endregion

		#region Public Members

		/// <summary>
		/// Connects driver to device. 
		/// <para>Call to this method establish communication with device. The connection status will be set based on the connection response from device.</para> 
		/// </summary>
		public void Connect()
		{
			_deviceFactory.DeviceStatusChanged -= Factory_PairedDeviceStatusChanged;
			_deviceFactory.DeviceStatusChanged += Factory_PairedDeviceStatusChanged;
			SendDeviceDiscoveryRequest();
		}

		/// <summary>
		/// Disconnects driver to device. 
		/// <para>Call to this method disconnects driver communication with device.</para> 
		/// </summary>
		public void Disconnect()
		{
			_deviceFactory.DeviceStatusChanged -= Factory_PairedDeviceStatusChanged;
		}

		/// <summary>
		/// Cleans up any resources created within the protocol including paired devices
		/// </summary>
		/// <para>
		/// When the platform protocol is being disposed, all paired devices must be cleaned up
		/// Paired devices must be disposed of by the Platform Driver since they 
		/// were created and are managed by the Platform Driver
		/// </para>
		public override void Dispose()
		{
			try
			{
				_pairedDevicesLock.Enter();

				foreach (var pairedDevice in _pairedDevices.Values)
				{
					if (pairedDevice is IDisposable)
						((IDisposable) pairedDevice).Dispose();
				}

				_pairedDevices.Clear();
			}
			finally
			{
				_pairedDevicesLock.Leave();
			}

			base.Dispose();
		}	
		#endregion

		#region Private Members

		private void SendDeviceDiscoveryRequest()
		{
			var command = new CommandSet("DeviceDiscovery", "DiscoverDevices", CommonCommandGroupType.Other, null, false,
				CommandPriority.Normal, StandardCommandsEnum.NotAStandardCommand);

			SendCommand(command);
		}

		private void Factory_PairedDeviceStatusChanged(object sender, SampleDeviceFactoryStatusEventArgs args)
		{
			switch (args.Status)
			{
				case DeviceStatus.Added:
					AddSamplePairedDevice(args.Device);
					break;
				case DeviceStatus.Updated:
					UpdateSamplePairedDevice(args.Device);
					break;
				case DeviceStatus.Removed:
					RemovePairedDevice(args.Device);
					break;
			}
		}

		private void AddSamplePairedDevice(IPairedDevice pairedDevice)
		{
			// Set connection status on device if the device created after the gateway is online.
			pairedDevice.SetConnectionStatus(IsConnected);

			AddPairedDevice(pairedDevice.PairedDeviceInformation, pairedDevice as ABasicDriver);

			try
			{
				_pairedDevicesLock.Enter();
				_pairedDevices[pairedDevice.PairedDeviceInformation.Id] = pairedDevice;
			}
			finally
			{
				_pairedDevicesLock.Leave();
			}
		}

		private void UpdateSamplePairedDevice(IPairedDevice updatedPairedDevice)
		{
			updatedPairedDevice.SetConnectionStatus(IsConnected);
			UpdatePairedDevice(updatedPairedDevice.PairedDeviceInformation.Id, updatedPairedDevice.PairedDeviceInformation);

			//if the updated paired device is a different instance than the current paired device in the cache
			//update the cache and dispose of the old paired device
			IPairedDevice oldPairedDevice;
			bool oldDeviceNeedsDisposal = false;
			try
			{
				_pairedDevicesLock.Enter();

				if (_pairedDevices.TryGetValue(updatedPairedDevice.PairedDeviceInformation.Id, out oldPairedDevice))
				{
					if (oldPairedDevice == updatedPairedDevice)
						return;

					//Values are different, need to update the cache
					_pairedDevices[updatedPairedDevice.PairedDeviceInformation.Id] = updatedPairedDevice;

					oldDeviceNeedsDisposal = true;
				}
			}
			finally
			{
				_pairedDevicesLock.Leave();
			}

			//Dispose of the old device if necessary
			if(oldDeviceNeedsDisposal && oldPairedDevice is IDisposable)
				((IDisposable) oldPairedDevice).Dispose();
		}

		private void RemovePairedDevice(IPairedDevice pairedDevice)
		{
			try
			{
				_pairedDevicesLock.Enter();

				if (_pairedDevices.ContainsKey(pairedDevice.PairedDeviceInformation.Id))
				{
					RemovePairedDevice(pairedDevice.PairedDeviceInformation.Id);

					//Remove the paired device from the local collection
					_pairedDevices.Remove(pairedDevice.PairedDeviceInformation.Id);
				}
			}
			finally
			{
				_pairedDevicesLock.Leave();
			}
 
			//Dispose of the paired device
			if (pairedDevice is IDisposable)
				((IDisposable)pairedDevice).Dispose();
			
		}

		private ValidatedRxData GatewayValidateResponse(string response, CommonCommandGroupType commandGroup)
		{
			return new ValidatedRxData(true, "DiscoverDevicesResponse");
		}

		#endregion
	}
}
