using Crestron.RAD.Common.Interfaces;
using Crestron.RAD.DeviceTypes.Gateway;
using Crestron.SimplSharp;

// ReSharper disable once CheckNamespace
namespace Crestron.Samples.Platforms
{
	/// <summary>
	/// Sample gateway device driver communicates to device through TCP connection.
	/// </summary>
	public class SampleGatewayTcp : AGateway, ITcp
	{
		#region Base Members

		public void Initialize(IPAddress ipAddress, int port)
		{
			var transport = new SampleGatewayTcpTransport
			{
				EnableLogging = InternalEnableLogging,
				CustomLogger = InternalCustomLogger,
				EnableRxDebug = InternalEnableRxDebug,
				EnableTxDebug = InternalEnableTxDebug
			};

			ConnectionTransport = transport;

			Protocol = new SampleGatewayProtocol(ConnectionTransport, Id)
			{
				EnableLogging = InternalEnableLogging,
				CustomLogger = InternalCustomLogger
			};
		}

		public override void Connect()
		{
			base.Connect();
			((SampleGatewayProtocol) Protocol).Connect();
		}

		public override void Disconnect()
		{
			base.Disconnect();
			((SampleGatewayProtocol) Protocol).Disconnect();
		}

		#endregion
	}
}