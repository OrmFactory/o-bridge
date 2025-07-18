using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OBridge.Server;

public class AsyncBinaryReader
{
	private readonly Stream stream;
	private readonly CancellationToken token;
	private readonly byte[] buffer = new byte[8];

	public AsyncBinaryReader(Stream stream, CancellationToken token)
	{
		this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
		this.token = token;
	}

	public int MaxStringBytes { get; set; }

	public async Task<byte> ReadByteAsync()
	{
		await ReadExactAsync(buffer, 1).ConfigureAwait(false);
		return buffer[0];
	}

	public async Task<int> ReadInt32Async()
	{
		await ReadExactAsync(buffer, 4).ConfigureAwait(false);
		return BitConverter.ToInt32(buffer, 0);
	}

	public async Task<string> ReadStringAsync()
	{
		int length = await Read7BitEncodedIntAsync().ConfigureAwait(false);
		if (length == 0) return string.Empty;
		if (length > MaxStringBytes) throw new Exception($"String length ({length}) exceed max string bytes {MaxStringBytes}");

		byte[] strBuf = new byte[length];
		await ReadExactAsync(strBuf, length).ConfigureAwait(false);
		return Encoding.UTF8.GetString(strBuf);
	}

	private async Task<int> Read7BitEncodedIntAsync()
	{
		int count = 0;
		int shift = 0;

		while (true)
		{
			byte b = await ReadByteAsync().ConfigureAwait(false);
			count |= (b & 0x7F) << shift;
			if ((b & 0x80) == 0) break;

			shift += 7;
			if (shift >= 35)
				throw new FormatException("Too many bytes in 7-bit encoded int.");
		}

		return count;
	}

	public virtual async Task<byte[]> ReadBytesAsync(int count)
	{
		var result = new byte[count];
		await ReadExactAsync(result, count).ConfigureAwait(false);
		return result;
	}

	private int isReading = 0;

	private async Task ReadExactAsync(byte[] buf, int count)
	{
		if (Interlocked.Exchange(ref isReading, 1) != 0)
			throw new InvalidOperationException("Another read is already in progress.");

		try
		{
			int offset = 0;
			while (count > 0)
			{
				int read = await stream.ReadAsync(buf.AsMemory(offset, count), token).ConfigureAwait(false);
				if (read == 0)
					throw new EndOfStreamException();
				offset += read;
				count -= read;
			}
		}
		finally
		{
			Interlocked.Exchange(ref isReading, 0);
		}
	}
}
