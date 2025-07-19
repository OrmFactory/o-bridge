using System.Buffers.Binary;
using System.Text;

namespace OBridge.Server;

public class Response
{
	private readonly MemoryStream buffer = new();
	private readonly Encoding encoding = Encoding.UTF8;
	private readonly byte[] intBuffer = new byte[8];

	public Response(ResponseTypeEnum type)
	{
		buffer.WriteByte((byte)type);
	}

	public void WriteByte(byte value)
	{
		buffer.WriteByte(value);
	}

	public void WriteInt32(int value)
	{
		BinaryPrimitives.WriteInt32LittleEndian(intBuffer, value);
		buffer.Write(intBuffer, 0, 4);
	}

	public void WriteInt64(long value)
	{
		BinaryPrimitives.WriteInt64LittleEndian(intBuffer, value);
		buffer.Write(intBuffer, 0, 8);
	}

	public void Write7BitEncodedInt(int value)
	{
		uint v = (uint)value;
		while (v >= 0x80)
		{
			buffer.WriteByte((byte)(v | 0x80));
			v >>= 7;
		}
		buffer.WriteByte((byte)v);
	}

	public void WriteString(string value)
	{
		int byteCount = encoding.GetByteCount(value);
		Write7BitEncodedInt(byteCount);

		byte[] bytes = encoding.GetBytes(value);
		buffer.Write(bytes, 0, bytes.Length);
	}

	public async Task SendAsync(Stream output, CancellationToken token)
	{
		buffer.Position = 0;
		await buffer.CopyToAsync(output, token);
		await output.FlushAsync(token);
	}

	public byte[] ToArray() => buffer.ToArray();

	public void Reset()
	{
		buffer.SetLength(0);
	}
}

public enum ResponseTypeEnum
{
	ConnectionSuccess = 0,
	Error = 0x10,
	OracleQueryError = 0x20,
}