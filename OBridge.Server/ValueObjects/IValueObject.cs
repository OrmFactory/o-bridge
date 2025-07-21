using Oracle.ManagedDataAccess.Client;

namespace OBridge.Server.ValueObjects;

public interface IValueObject
{
	void LoadFromReader(OracleDataReader reader, int ordinal);
	void Serialize(Response row);
}

public class NumberValue : IValueObject
{
	private string number = "";

	public void LoadFromReader(OracleDataReader reader, int ordinal)
	{
		number = reader.GetString(ordinal);
	}

	public void Serialize(Response row)
	{
		if (int.TryParse(number, out var intVal) && intVal >= 0 && intVal <= 127)
		{
			row.WriteByte((byte)(0x80 | intVal));
			return;
		}

		var value = number;

		var negative = value.StartsWith("-");
		if (negative) value = value.Substring(1);

		int scale = 0;
		int dotIndex = value.IndexOf('.');
		if (dotIndex != -1)
		{
			scale = -(value.Length - dotIndex - 1);
			value = value.Remove(dotIndex, 1);
		}

		value = value.TrimStart('0');
		if (value == "") value = "0";

		int digitCount = value.Length;
		byte meta = (byte)(digitCount & 0x3F);
		if (negative) meta |= 0x40;
		row.WriteByte(meta);
		row.WriteByte((sbyte)scale);

		for (int i = 0; i < digitCount; i += 2)
		{
			byte high = (byte)(value[i] - '0');
			byte low = (i + 1 < digitCount) ? (byte)(value[i + 1] - '0') : (byte)0;
			byte bcd = (byte)((high << 4) | low);
			row.WriteByte(bcd);
		}
	}
}