using System.Buffers.Binary;
using System.Text;

namespace OBridge.Server;

public class AsyncBinaryWriter : IAsyncDisposable
{
	private readonly Stream stream;
	private readonly CancellationToken token;
	private readonly Encoding encoding = Encoding.UTF8;
	private readonly byte[] intBuffer = new byte[8];

	public AsyncBinaryWriter(Stream stream, CancellationToken token)
	{
		this.stream = stream;
		this.token = token;
	}

	public async Task WriteByteAsync(byte value)
	{
		intBuffer[0] = value;
		await stream.WriteAsync(intBuffer.AsMemory(0, 1), token);
	}

	public async Task WriteInt32Async(int value)
	{
		BinaryPrimitives.WriteInt32LittleEndian(intBuffer, value);
		await stream.WriteAsync(intBuffer.AsMemory(0, 4), token);
	}

	public async Task WriteInt64Async(long value)
	{
		BinaryPrimitives.WriteInt64LittleEndian(intBuffer, value);
		await stream.WriteAsync(intBuffer.AsMemory(0, 8), token);
	}

	public async Task Write7BitEncodedIntAsync(int value)
	{
		int i = 0;
		uint v = (uint)value;
		while (v >= 0x80)
		{
			intBuffer[i++] = (byte)(v | 0x80);
			v >>= 7;
		}
		intBuffer[i++] = (byte)v;
		await stream.WriteAsync(intBuffer.AsMemory(0, i), token);
	}

	public async Task WriteStringAsync(string value)
	{
		int byteCount = encoding.GetByteCount(value);
		await Write7BitEncodedIntAsync(byteCount);

		byte[] buffer = new byte[byteCount];
		encoding.GetBytes(value, 0, value.Length, buffer, 0);
		await stream.WriteAsync(buffer.AsMemory(0, byteCount), token);
	}

	public Task FlushAsync()
	{
		return stream.FlushAsync(token);
	}

	public async ValueTask DisposeAsync()
	{
		await FlushAsync();
	}
}