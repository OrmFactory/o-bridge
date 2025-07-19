using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using ZstdSharp;

namespace OBridge.Server;

public class Session : IAsyncDisposable
{
	private const int ProtocolVersion = 1;
	private readonly Stream stream;
	private readonly Settings settings;
	private readonly ILogger logger;
	private readonly CancellationTokenSource sessionCts;
	private readonly CancellationToken token;
	private bool enableCompression = false;
	private AsyncBinaryReader reader;
	private AsyncBinaryWriter writer;
	private CompressionStream? zstdStream;
	private ConnectionCredentials credentials;
	private OracleConnection connection;
	private Query? currentQuery;


	public Session(Stream stream, Settings settings, CancellationToken token, ILogger logger)
	{
		sessionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
		this.stream = stream;
		this.settings = settings;
		this.logger = logger;
		this.token = sessionCts.Token;
	}

	public async Task Process()
	{
		reader = new AsyncBinaryReader(stream, token);

		await ReadHeader();
		if (!settings.EnableCompression) enableCompression = false;

		credentials = await ReadCredentials();
		await TryConnect();

		ReportConnectionSuccess();

		if (enableCompression)
		{
			zstdStream = new CompressionStream(stream);
			writer = new AsyncBinaryWriter(zstdStream, token);
		}
		else
		{
			writer = new AsyncBinaryWriter(stream, token);
		}

		while (!token.IsCancellationRequested)
		{
			var command = await reader.ReadByteAsync();
			if (command == 0x30)
			{
				var query = currentQuery;
				if (query != null) await query.Stop();
			}

			if (command == 0x20)
			{
				var query = currentQuery;
				if (query != null) await query.Finish();

				currentQuery = new Query(connection, writer, token);
				await currentQuery.ReadQuery(reader);
				var task = currentQuery.Execute();
				_ = ExecuteQueryTask(task);
			}
		}
	}

	private async Task ExecuteQueryTask(Task task)
	{
		try
		{
			await task;
		}
		catch (OperationCanceledException)
		{
			await ReportError(ErrorCode.QueryCancelledByClient, "Cancelled by client");
		}
		catch (Exception e)
		{
			logger.LogError(e, "Failed execute query");
			await sessionCts.CancelAsync();
		}
	}

	private async Task TryConnect()
	{
		if (credentials.ConnectionString == "")
		{
			await ReportError(ErrorCode.ConnectionModeDisabled, "Internal mode is not implemented");
			throw new NotImplementedException("Internal mode is not implemented");
		}

		if (credentials.ConnectionString != "" && !settings.EnableFullProxy)
		{
			await ReportError(ErrorCode.ConnectionModeDisabled, "Full proxy mode is disabled");
			throw new Exception("Full proxy mode is disabled");
		}

		try
		{
			connection = new OracleConnection(credentials.ConnectionString);
			await connection.OpenAsync(token);
		}
		catch (Exception e)
		{
			await ReportError(ErrorCode.ConnectionFailed, e.Message);
			throw e;
		}

	}

	private async Task ReportConnectionSuccess()
	{
		await writer.WriteByteAsync(0x00);
		byte compressionFlag = 0;
		if (enableCompression) compressionFlag = 1;
		await writer.WriteByteAsync(compressionFlag);
		await writer.WriteByteAsync(ProtocolVersion);
		await writer.FlushAsync();
	}

	private async Task ReportError(ErrorCode errorCode, string message)
	{
		await writer.WriteByteAsync(0x10);
		await writer.WriteByteAsync((byte)errorCode);
		await writer.WriteStringAsync(message);
		await writer.FlushAsync();
	}

	private async Task<ConnectionCredentials> ReadCredentials()
	{
		var credType = await reader.ReadByteAsync();
		if (credType == 2)
		{
			reader.MaxStringBytes = 1024;
			var srv = await reader.ReadStringAsync();
			var login = await reader.ReadStringAsync();
			var password = await reader.ReadStringAsync();
			return new ConnectionCredentials(srv, login, password);
		}

		if (credType == 3)
		{
			reader.MaxStringBytes = 4096;
			var connectionString = await reader.ReadStringAsync();
			return new ConnectionCredentials(connectionString);
		}

		throw new Exception("wrong connection credentials type " + credType);
	}

	private async Task ReadHeader()
	{
		var header = await reader.ReadBytesAsync(8);
		var wrongHeaderEx = new Exception("Wrong header");
		if (header[0] != 0x4F) throw wrongHeaderEx;
		if (header[1] != 0x43) throw wrongHeaderEx;
		if (header[2] != 0x4F) throw wrongHeaderEx;
		if (header[3] != 0x4E) throw wrongHeaderEx;

		if (header[5] == 1) enableCompression = true;
	}

	public async ValueTask DisposeAsync()
	{
		sessionCts.Dispose();
		if (currentQuery != null) await currentQuery.Stop();
		if (connection != null) await connection.DisposeAsync();
		if (writer != null) await writer.DisposeAsync();
		if (zstdStream != null) await zstdStream.DisposeAsync();
	}
}

public class ConnectionCredentials
{
	private readonly string srv;
	private readonly string login;
	private readonly string password;
	public bool InternalCredentials { get; } = false;

	public string ConnectionString { get; } = "";

	public ConnectionCredentials(string srv, string login, string password)
	{
		this.srv = srv;
		this.login = login;
		this.password = password;
		InternalCredentials = true;
	}

	public ConnectionCredentials(string connectionString)
	{
		ConnectionString = connectionString;
	}
}

public enum ErrorCode
{
	ConnectionModeDisabled,
	ConnectionFailed,
	QueryCancelledByClient
}