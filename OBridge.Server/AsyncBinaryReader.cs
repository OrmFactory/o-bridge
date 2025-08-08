using System;
using System.Buffers.Binary;
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

	public async Task<byte> ReadByte()
	{
		await ReadExact(buffer, 1).ConfigureAwait(false);
		return buffer[0];
	}

	public async Task<int> ReadInt32()
	{
		await ReadExact(buffer, 4).ConfigureAwait(false);
		return BitConverter.ToInt32(buffer, 0);
	}

	public async Task<long> ReadInt64()
	{
		await ReadExact(buffer, 8).ConfigureAwait(false);
		return BitConverter.ToInt64(buffer, 0);
	}

	public async Task<uint> ReadUInt32()
	{
		await ReadExact(buffer, 4).ConfigureAwait(false);
		return BitConverter.ToUInt32(buffer, 0);
	}

	public async Task<short> ReadInt16()
	{
		await ReadExact(buffer, 2).ConfigureAwait(false);
		return BitConverter.ToInt16(buffer, 0);
	}

	public async Task<ushort> ReadUInt16()
	{
		await ReadExact(buffer, 2).ConfigureAwait(false);
		return BitConverter.ToUInt16(buffer, 0);
	}

	public async Task<float> ReadFloat()
	{
		await ReadExact(buffer, 4).ConfigureAwait(false);
		return BinaryPrimitives.ReadSingleLittleEndian(buffer);
	}

	public async Task<double> ReadDouble()
	{
		await ReadExact(buffer, 8).ConfigureAwait(false);
		return BinaryPrimitives.ReadDoubleLittleEndian(buffer);
	}

	public async Task<bool> ReadBoolean()
	{
		var b = await ReadByte().ConfigureAwait(false);
		return b > 0;
	}

	public async Task<decimal> ReadDecimal()
	{
		int lo = await ReadInt32().ConfigureAwait(false);
		int mid = await ReadInt32().ConfigureAwait(false);
		int hi = await ReadInt32().ConfigureAwait(false);
		int flags = await ReadInt32().ConfigureAwait(false);
		return new decimal(new[] { lo, mid, hi, flags });
	}

	public async Task<DateTime> ReadDateTime()
	{
		var ticks = await ReadInt64().ConfigureAwait(false);
		return new DateTime(ticks);
	}

	public async Task<string> ReadString()
	{
		int length = await Read7BitEncodedInt().ConfigureAwait(false);
		if (length == 0) return string.Empty;
		if (length > MaxStringBytes) throw new Exception($"String length ({length}) exceed max string bytes {MaxStringBytes}");

		byte[] strBuf = new byte[length];
		await ReadExact(strBuf, length).ConfigureAwait(false);
		return Encoding.UTF8.GetString(strBuf);
	}

	public async Task<byte[]> ReadBinary()
	{
		var len = await Read7BitEncodedInt().ConfigureAwait(false);
		return await ReadBytes(len).ConfigureAwait(false);
	}

	public async Task<int> Read7BitEncodedInt()
	{
		int count = 0;
		int shift = 0;

		while (true)
		{
			byte b = await ReadByte().ConfigureAwait(false);
			count |= (b & 0x7F) << shift;
			if ((b & 0x80) == 0) break;

			shift += 7;
			if (shift >= 35)
				throw new FormatException("Too many bytes in 7-bit encoded int.");
		}

		return count;
	}

	public virtual async Task<byte[]> ReadBytes(int count)
	{
		var result = new byte[count];
		await ReadExact(result, count).ConfigureAwait(false);
		return result;
	}

	private int isReading = 0;

	private async Task ReadExact(byte[] buf, int count)
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
