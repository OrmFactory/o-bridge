using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace OBridge.Server.Config;

public class Settings
{
	public bool EnableFullProxy = true;
	public bool EnableCompression = true;
	public int PlainListenerPort = 0x0F0F;
	public int SslListenerPort = 0x0FAC;
	public string? CertificatePath = null;

	public List<Server> Servers = new();

	public static Settings LoadSettings(ILogger logger)
	{
		var fileName = "config/config.yaml";
		var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

		if (!File.Exists(fullPath))
		{
			logger.LogInformation("No config.yaml found in /config directory, using defaults. See config.sample.yaml for example.");
			return new Settings();
		}

		try
		{
			var yaml = File.ReadAllText(fullPath);
			var deserializer = new DeserializerBuilder()
				.IgnoreUnmatchedProperties()
				.Build();
			return deserializer.Deserialize<Settings>(yaml) ?? new Settings();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to parse config.yaml. Falling back to defaults.");
			return new Settings();
		}
	}
}

public class Server
{
	public string ServerName;
	public string OracleHost;
	public int? OraclePort;
	public string? OracleSID;
	public string? OracleServiceName;
	public string? OracleUser;
	public string? OraclePassword;

	public List<User> Users = new();
}

public class User
{
	public string Name;
	public string Password;
}