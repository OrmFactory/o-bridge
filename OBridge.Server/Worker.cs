using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.ConstrainedExecution;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace OBridge.Server;

public class Worker : BackgroundService
{
	private readonly ILogger logger;
	private Settings settings;
	private X509Certificate2 certificate;

	public Worker(ILoggerFactory loggerFactory)
	{
		logger = loggerFactory.CreateLogger("");
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			settings = Settings.LoadSettings(logger);
			certificate = Certificate.LoadCertificate(settings, logger);

			var fingerprint = string.Join(":",
				Enumerable.Range(0, certificate.Thumbprint.Length / 2)
					.Select(i => certificate.Thumbprint.Substring(i * 2, 2)));
			logger.LogInformation("TLS certificate fingerprint: " + fingerprint);
		}
		catch (Exception e)
		{
			logger.LogError(e, e.Message);
			return;
		}

		logger.LogInformation("Starting TCP listeners...");

		await Task.WhenAll(
			ListenPlain(settings.PlainListenerPort, stoppingToken), 
			ListenTls(settings.SslListenerPort, stoppingToken));

		logger.LogInformation("Stopping TCP listeners...");
	}

	private async Task ListenPlain(int port, CancellationToken token)
	{
		var listener = new TcpListener(IPAddress.Any, port);
		listener.Start();
		logger.LogInformation($"Listening on port {port}");

		while (!token.IsCancellationRequested)
		{
			var client = await listener.AcceptTcpClientAsync(token);
			Task.Run(() => HandlePlainClientAsync(client, token), token);
		}
	}

	private async Task ListenTls(int port, CancellationToken token)
	{
		var listener = new TcpListener(IPAddress.Any, port);
		listener.Start();
		logger.LogInformation($"Listening on port {port} using TLS");

		while (!token.IsCancellationRequested)
		{
			var client = await listener.AcceptTcpClientAsync(token);
			Task.Run(() => HandleSslClientAsync(client, token), token);
		}
		listener.Stop();
		logger.LogInformation($"TLS port {port} closed");
	}

	private async Task HandlePlainClientAsync(TcpClient client, CancellationToken token)
	{
		await using var networkStream = client.GetStream();
		var remote = client.Client.RemoteEndPoint;
		logger.LogDebug($"Client {remote} connected");

		try
		{
			await using var session = new Session(networkStream, settings, token);
			await session.Process();
		}
		catch (Exception e)
		{
			logger.LogError(e, e.Message);
		}
		finally
		{
			logger.LogDebug($"Client {remote} disconnected");
		}
	}

	private async Task HandleSslClientAsync(TcpClient client, CancellationToken token)
	{
		await using var networkStream = client.GetStream();
		var remote = client.Client.RemoteEndPoint;
		logger.LogDebug($"Client {remote} connected with SSL");

		await using var ssl = new SslStream(networkStream);

		try
		{
			await ssl.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);
			await using var session = new Session(ssl, settings, token);
			await session.Process();
		}
		catch (Exception e)
		{
			logger.LogError(e, e.Message);
		}
		finally
		{
			logger.LogDebug($"Client {remote} disconnected");
		}
	}
}