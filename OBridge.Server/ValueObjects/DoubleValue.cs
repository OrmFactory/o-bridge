using Oracle.ManagedDataAccess.Client;

namespace OBridge.Server.ValueObjects;

public class DoubleValue : IValueObject
{
	private double value;

	public void LoadFromReader(OracleDataReader reader, int ordinal)
	{
		value = reader.GetDouble(ordinal);
	}

	public void Serialize(Response row)
	{
		row.WriteDouble(value);
	}
}