using System.Buffers.Binary;
using System.IO;
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

	public void WriteBoolean(bool value)
	{
		WriteByte((byte)(value ? 1 : 0));
	}

	public void WriteInt16(short value)
	{
		BinaryPrimitives.WriteInt16LittleEndian(intBuffer, value);
		buffer.Write(intBuffer, 0, 2);
	}

	public void WriteUInt16(ushort value)
	{
		BinaryPrimitives.WriteUInt16LittleEndian(intBuffer, value);
		buffer.Write(intBuffer, 0, 2);
	}

	public void WriteInt32(int value)
	{
		BinaryPrimitives.WriteInt32LittleEndian(intBuffer, value);
		buffer.Write(intBuffer, 0, 4);
	}

	public void WriteUInt32(uint value)
	{
		BinaryPrimitives.WriteUInt32LittleEndian(intBuffer, value);
		buffer.Write(intBuffer, 0, 4);
	}

	public void WriteInt64(long value)
	{
		BinaryPrimitives.WriteInt64LittleEndian(intBuffer, value);
		buffer.Write(intBuffer, 0, 8);
	}

	public void WriteUInt64(ulong value)
	{
		BinaryPrimitives.WriteUInt64LittleEndian(intBuffer, value);
		buffer.Write(intBuffer, 0, 8);
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
			buffer.WriteByte((byte)(v | 0x80));
			v >>= 7;
		}
		buffer.WriteByte((byte)v);
	}

	public void WriteString(string value)
	{
		var bytes = encoding.GetBytes(value);
		Write7BitEncodedInt(bytes.Length);
		buffer.Write(bytes, 0, bytes.Length);
	}

	public async Task SendAsync(Stream output, CancellationToken token)
	{
		buffer.Position = 0;
		await buffer.CopyToAsync(output, token);
	}

	public byte[] ToArray() => buffer.ToArray();

	public void Reset()
	{
		buffer.SetLength(0);
	}

	public void WriteByte(sbyte value)
	{
		WriteByte(unchecked((byte)value));
	}

	public void WriteFloat(float value)
	{
		BinaryPrimitives.WriteSingleLittleEndian(intBuffer, value);
		buffer.Write(intBuffer, 0, 4);
	}

	public void WriteDouble(double value)
	{
		BinaryPrimitives.WriteDoubleLittleEndian(intBuffer, value);
		buffer.Write(intBuffer, 0, 8);
	}

	public void WriteBytes(params byte[] bytes)
	{
		buffer.Write(bytes, 0, bytes.Length);
	}

	public void WriteBytes(byte[] bytes, int length)
	{
		buffer.Write(bytes, 0, length);
	}
}

public enum ResponseTypeEnum
{
	ConnectionSuccess = 0,
	TableHeader = 0x01,
	RowData = 0x02,
	EndOfRowStream = 0x03,
	Error = 0x10,
	OracleQueryError = 0x11,
}