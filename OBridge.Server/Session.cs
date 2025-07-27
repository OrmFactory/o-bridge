using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;

namespace OBridge.Server;

public class Session : IAsyncDisposable
{
	private const int ProtocolVersion = 1;
	
	private readonly Settings settings;
	private readonly ILogger logger;
	private readonly CancellationTokenSource sessionCts;
	private readonly CancellationToken token;
	private bool enableCompression = false;
	private AsyncBinaryReader reader;
	private CompressionStream? zstdStream;
	private ConnectionCredentials credentials;
	private OracleConnection connection;
	private Query? currentQuery;

	private Stream stream;

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

		await ReportConnectionSuccess();

		if (enableCompression)
		{
			stream = new CompressionStream(stream);
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

				currentQuery = new Query(connection, stream, token);
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
			await ReportError(ErrorCodeEnum.QueryCancelledByClient, "Cancelled by client");
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
			await ReportError(ErrorCodeEnum.ConnectionModeDisabled, "Internal mode is not implemented");
			throw new NotImplementedException("Internal mode is not implemented");
		}

		if (credentials.ConnectionString != "" && !settings.EnableFullProxy)
		{
			await ReportError(ErrorCodeEnum.ConnectionModeDisabled, "Full proxy mode is disabled");
			throw new Exception("Full proxy mode is disabled");
		}

		try
		{
			connection = new OracleConnection(credentials.ConnectionString);
			await connection.OpenAsync(token);
			await SetUtcTimeZone();
		}
		catch (Exception e)
		{
			await ReportError(ErrorCodeEnum.ConnectionFailed, e.Message);
			throw e;
		}
	}

	private async Task SetUtcTimeZone()
	{
		await using var command = connection.CreateCommand();
		command.CommandText = "ALTER SESSION SET TIME_ZONE = 'UTC'";
		await command.ExecuteNonQueryAsync(token);
	}

	private async Task ReportConnectionSuccess()
	{
		var response = new Response(0x00);
		byte compressionFlag = 0;
		if (enableCompression) compressionFlag = 1;
		response.WriteByte(compressionFlag);
		response.WriteByte(ProtocolVersion);
		await response.SendAsync(stream, token);
		await stream.FlushAsync(token);
	}

	private async Task ReportError(ErrorCodeEnum errorCode, string message)
	{
		var response = new Response(ResponseTypeEnum.Error);
		response.WriteByte((byte)errorCode);
		response.WriteString(message);
		await response.SendAsync(stream, token);
		await stream.FlushAsync(token);
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

public enum ErrorCodeEnum
{
	ConnectionModeDisabled = 0x01,
	ConnectionFailed = 0x02,
	QueryCancelledByClient = 0x20,
	QueryExecutionFailed = 0x30
}