using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OBridge.Server;

public class Worker : BackgroundService
{
	private const int ListenerPort = 0x0FAC;

	private readonly ILogger<Worker> logger;

	public Worker(ILogger<Worker> logger)
	{
		this.logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		logger.LogInformation("Starting TCP listener...");
		var listener = new TcpListener(IPAddress.Any, ListenerPort);
		listener.Start();
		while (true)
		{
			var client = await listener.AcceptTcpClientAsync(stoppingToken);
			Task.Run(() => HandleClientAsync(client, stoppingToken));
		}
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