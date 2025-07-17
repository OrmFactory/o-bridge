using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ZstdSharp;

namespace OBridge.Server;

public class Session : IDisposable
{
	private readonly Stream stream;
	private readonly Settings settings;
	private readonly CancellationToken token;
	private bool enableCompression = false;
	private BinaryReader reader;
	private BinaryWriter writer;
	private CompressionStream? zstdStream;
	private ConnectionCredentials credentials;

	public Session(Stream stream, Settings settings, CancellationToken token)
	{
		this.stream = stream;
		this.settings = settings;
		this.token = token;
	}

	public async Task Process()
	{
		reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

		ReadHeader();

		if (enableCompression)
		{
			zstdStream = new CompressionStream(stream);
			writer = new BinaryWriter(zstdStream, Encoding.UTF8);
		}
		else
		{
			writer = new BinaryWriter(stream, Encoding.UTF8);
		}

		credentials = ReadCredentials();

		if (credentials.ConnectionString == "")
		{
			ReportError(ErrorCode.ConnectionModeDisabled, "Internal mode not implemented");
		}

		while (!token.IsCancellationRequested)
		{
		}
	}

	private void ReportError(ErrorCode errorCode, string message)
	{
		writer.Write((byte)0x10);
		writer.Write((byte)errorCode);
		writer.Write(message);
		writer.Flush();
		throw new Exception(message);
	}

	private ConnectionCredentials ReadCredentials()
	{
		var credType = reader.ReadByte();
		if (credType == 2)
		{
			var srv = reader.ReadString();
			var login = reader.ReadString();
			var password = reader.ReadString();
			return new ConnectionCredentials(srv, login, password);
		}

		if (credType == 3)
		{
			var connectionString = reader.ReadString();
			return new ConnectionCredentials(connectionString);
		}

		throw new Exception("wrong connection credentials type " + credType);
	}

	private void ReadHeader()
	{
		var header = reader.ReadBytes(8);
		var wrongHeaderEx = new Exception("Wrong header");
		if (header[0] != 0x4F) throw wrongHeaderEx;
		if (header[1] != 0x43) throw wrongHeaderEx;
		if (header[2] != 0x4F) throw wrongHeaderEx;
		if (header[3] != 0x4E) throw wrongHeaderEx;

		if (header[5] == 1) enableCompression = true;
	}

	public void Dispose()
	{
		reader?.Close();
		writer?.Close();
		zstdStream?.Close();
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
	ConnectionModeDisabled
}