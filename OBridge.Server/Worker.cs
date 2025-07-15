using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace OBridge.Server;

public class Worker : BackgroundService
{
	private readonly ILogger<Worker> logger;
	private Settings settings;
	private X509Certificate2 certificate;

	public Worker(ILogger<Worker> logger)
	{
		this.logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			settings = Settings.LoadSettings(logger);
			certificate = Certificate.LoadCertificate(settings, logger);
		}
		catch (Exception e)
		{
			logger.LogError(e, e.Message);
			return;
		}

		logger.LogInformation("Starting TCP listeners...");
		var t1 = Task.Run(() => ListenPlain(settings.PlainListenerPort, stoppingToken), stoppingToken);
		var t2 = Task.Run(() => ListenTls(settings.SslListenerPort, stoppingToken), stoppingToken);
		Task.WaitAll(t1, t2);

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

	}

	private async Task HandleSslClientAsync(TcpClient client, CancellationToken token)
	{

	}

	private async Task HandleClientAsync(TcpClient client, CancellationToken token)
	{
		await using var stream = client.GetStream();
		var remote = client.Client.RemoteEndPoint;
		logger.LogDebug($"[Client Connected] {remote}");

		try
		{
			while (true)
			{
				var header = await ReadExactAsync(stream, 4);
				if (header == null) break;

				int length = BitConverter.ToInt32(header);
				byte type = (byte)stream.ReadByte();
				if (type == -1) break;

				var payload = await ReadExactAsync(stream, length - 1);
				if (payload == null) break;

				switch (type)
				{
					case 0x01:
						string sql = Encoding.UTF8.GetString(payload);
						Console.WriteLine($"[Query] {sql}");

						var result = "SELECT OK"u8.ToArray();
						await SendFrameAsync(stream, 0x02, result);
						break;

					default:
						logger.LogError($"[Unknown Frame] Type={type}");
						break;
				}
			}
		}
		catch (Exception ex)
		{
			logger.LogError($"[Error] {ex.Message}");
		}
		finally
		{
			logger.LogDebug($"[Client Disconnected] {remote}");
			client.Close();
		}
	}

	static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int count)
	{
		var buffer = new byte[count];
		int offset = 0;
		while (offset < count)
		{
			int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
			if (read == 0) return null;
			offset += read;
		}
		return buffer;
	}

	static async Task SendFrameAsync(NetworkStream stream, byte type, byte[] data)
	{
		int length = data.Length + 1;
		await stream.WriteAsync(BitConverter.GetBytes(length));
		await stream.WriteAsync([type], 0, 1);
		await stream.WriteAsync(data);
	}
}