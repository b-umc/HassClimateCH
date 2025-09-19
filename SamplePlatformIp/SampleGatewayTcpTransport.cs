using Crestron.RAD.Common.Transports;

namespace Crestron.Samples.Platforms
{
	public class SampleGatewayTcpTransport : ATransportDriver
	{
		public override void SendMethod(string message, object[] paramaters)
		{
			// Based on the communication type, requests to the gateway device will be sent here.

			if (message == "DiscoverDevices")
			{
				// Send logic for Discovering devices goes here. 
				// DataHandler dispatch responses from the device to protocol for processing.
				DataHandler("DiscoverDevicesResponse");
			}
		}

		public override void Start()
		{
			// Starts communication with device. 
			// This will start the underlying connection client (COM, HttpClient..etc) based on communication type supported by the device.
			ConnectionChanged(true);
		}

		public override void Stop()
		{
			//Stops communication with device.
			ConnectionChanged(false);
		}
	}
}