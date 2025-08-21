using Oracle.ManagedDataAccess.Client;

namespace OBridge.Server.ValueObjects;

public class NumberValue : IValueObject
{
	private string number = "";

	public void LoadFromReader(OracleDataReader reader, int ordinal)
	{
		number = reader.GetString(ordinal);
	}

	public void Serialize(Response row)
	{
		// Format A: single-byte unsigned integer
		if (number.Length < 4 && int.TryParse(number, out var intVal) && intVal >= 0 && intVal <= 127)
		{
			row.WriteByte((byte)(0x80 | intVal));
			return;
		}

		var value = number;
		var negative = value.Length > 0 && value[0] == '-';
		if (negative) value = value.Substring(1);

		int scale = 0;
		int dotIndex = value.IndexOf('.');
		if (dotIndex != -1)
		{
			scale = -(value.Length - dotIndex - 1);
			value = value.Remove(dotIndex, 1);
		}

		// Trim leading zeros
		value = value.TrimStart('0');
		if (value == "")
		{
			value = "0";
			scale = 0;
		}

		// Trim trailing zeros for base-100 compression
		int trailingZeros = 0;
		for (int i = value.Length - 1; i >= 0 && value[i] == '0'; i--)
		{
			trailingZeros++;
		}
		if (trailingZeros > 0)
		{
			value = value.Substring(0, value.Length - trailingZeros);
			scale += trailingZeros;
		}

		if ((value.Length & 1) == 1) value = "0" + value;
			
		int digitCount = value.Length;
		int scaleBias = scale + 32;
		var fallback = scaleBias is < 0 or > 62;

		byte meta = 0x00;
		if (negative) meta |= 0x40;
		meta |= (byte)(fallback ? 63 : scaleBias);
		row.WriteByte(meta);
		if (fallback)
		{
			row.WriteByte((byte)(scale + 130));
		}

		// Write base-100 digits, big-endian, mark last byte
		for (int i = 0; i < digitCount; i += 2)
		{
			byte hi = (byte)(value[i] - '0');
			byte lo = (byte)(value[i + 1] - '0');
			byte b100 = (byte)(hi * 10 + lo);

			if (i + 2 >= digitCount) b100 |= 0x80;

			row.WriteByte(b100);
		}
	}

	public string GetDefaultTypeName() => "NUMBER";
}