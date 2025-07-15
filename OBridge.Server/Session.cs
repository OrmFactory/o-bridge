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
	private readonly CancellationToken token;
	private bool enableCompression = false;
	private BinaryReader reader;
	private BinaryWriter writer;
	private CompressionStream zstdStream;

	public Session(Stream stream, CancellationToken token)
	{
		this.stream = stream;
		this.token = token;
	}

	public async Task Process()
	{
		reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

		ReadHeader(reader);

		if (enableCompression)
		{
			zstdStream = new CompressionStream(stream);
			writer = new BinaryWriter(zstdStream, Encoding.UTF8);
		}
		else
		{
			writer = new BinaryWriter(stream, Encoding.UTF8);
		}

		while (!token.IsCancellationRequested)
		{
			
		}
	}

	private void ReadHeader(BinaryReader reader)
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