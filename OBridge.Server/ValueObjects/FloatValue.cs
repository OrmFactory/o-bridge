using Oracle.ManagedDataAccess.Client;

namespace OBridge.Server.ValueObjects;

public class FloatValue : IValueObject
{
	private float value;

	public void LoadFromReader(OracleDataReader reader, int ordinal)
	{
		value = reader.GetFloat(ordinal);
	}

	public void Serialize(Response row)
	{
		row.WriteFloat(value);
	}
}