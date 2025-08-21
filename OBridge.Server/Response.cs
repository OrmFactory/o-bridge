using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace OBridge.Server;

public class Response
{
	private readonly ArrayBufferWriter<byte> buffer = new();
	private static readonly Encoding Encoding = Encoding.UTF8;

	public int WrittenBytesCount => buffer.WrittenCount;

	public Response() {}

	public Response(ResponseTypeEnum type)
	{
		WriteByte((byte)type);
	}

	public void WriteByte(byte value)
	{
		var span = buffer.GetSpan(1);
		span[0] = value;
		buffer.Advance(1);
	}

	public void WriteBoolean(bool value)
	{
		WriteByte((byte)(value ? 1 : 0));
	}

	public void WriteInt16(short value)
	{
		var span = buffer.GetSpan(2);
		BinaryPrimitives.WriteInt16LittleEndian(span, value);
		buffer.Advance(2);
	}

	public void WriteUInt16(ushort value)
	{
		var span = buffer.GetSpan(2);
		BinaryPrimitives.WriteUInt16LittleEndian(span, value);
		buffer.Advance(2);
	}

	public void WriteInt32(int value)
	{
		var span = buffer.GetSpan(4);
		BinaryPrimitives.WriteInt32LittleEndian(span, value);
		buffer.Advance(4);
	}

	public void WriteUInt32(uint value)
	{
		var span = buffer.GetSpan(4);
		BinaryPrimitives.WriteUInt32LittleEndian(span, value);
		buffer.Advance(4);
	}

	public void WriteInt64(long value)
	{
		var span = buffer.GetSpan(8);
		BinaryPrimitives.WriteInt64LittleEndian(span, value);
		buffer.Advance(8);
	}

	public void WriteUInt64(ulong value)
	{
		var span = buffer.GetSpan(8);
		BinaryPrimitives.WriteUInt64LittleEndian(span, value);
		buffer.Advance(8);
	}

	public void WriteDateTime(DateTime value)
	{
		WriteInt64(value.Ticks);
	}

	public void WriteDecimal(decimal value)
	{
		int[] bits = decimal.GetBits(value);
		foreach (int part in bits)
			WriteInt32(part);
	}

	public void Write7BitEncodedInt(int value)
	{
		uint v = (uint)value;
		while (v >= 0x80)
		{
			WriteByte((byte)(v | 0x80));
			v >>= 7;
		}
		WriteByte((byte)v);
	}

	public void WriteString(string value)
	{
		var byteCount = Encoding.GetByteCount(value);
		Write7BitEncodedInt(byteCount);

		var span = buffer.GetSpan(byteCount);
		var written = Encoding.GetBytes(value.AsSpan(), span);
		buffer.Advance(written);
	}

	public async Task SendAsync(Stream output, CancellationToken token)
	{
		await output.WriteAsync(buffer.WrittenMemory, token).ConfigureAwait(false);
	}

	public void Reset()
	{
		buffer.Clear();
	}

	public void WriteByte(sbyte value)
	{
		WriteByte(unchecked((byte)value));
	}

	public void WriteFloat(float value)
	{
		var span = buffer.GetSpan(4);
		BinaryPrimitives.WriteSingleLittleEndian(span, value);
		buffer.Advance(4);
	}

	public void WriteDouble(double value)
	{
		var span = buffer.GetSpan(8);
		BinaryPrimitives.WriteDoubleLittleEndian(span, value);
		buffer.Advance(8);
	}

	public void WriteBytes(byte[] bytes)
	{
		buffer.Write(bytes.AsSpan());
	}
}

public enum ResponseTypeEnum
{
	ConnectionSuccess = 0,
	TableHeader = 0x01,
	RowDataBatch = 0x02,
	EndOfRowStream = 0x03,
	Error = 0x10,
	OracleQueryError = 0x11,
}