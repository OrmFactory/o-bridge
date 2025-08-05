using Oracle.ManagedDataAccess.Client;

namespace OBridge.Server.ValueObjects;

public class BooleanValue : IValueObject
{
	private bool value;

	public void LoadFromReader(OracleDataReader reader, int ordinal)
	{
		value = reader.GetBoolean(ordinal);
	}

	public void Serialize(Response row)
	{
		row.WriteByte(value ? (byte)1 : (byte)0);
	}

	public string GetDefaultTypeName() => "BOOLEAN";
}