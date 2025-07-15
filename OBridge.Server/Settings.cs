using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OBridge.Server;

public class Settings
{
	public bool EnableFullProxy = true;
	public int PlainListenerPort = 0x0F0F;
	public int SslListenerPort = 0x0FAC;
	public string? CertificatePath = null;

	public static Settings LoadSettings(ILogger logger)
	{
		logger.LogInformation("Loading settings is not implemented, using defaults");
		return new Settings();
	}
}