using Oracle.ManagedDataAccess.Client;

namespace OBridge.Server.ValueObjects;

public class StringValue : IValueObject
{
	private string value;

	public void LoadFromReader(OracleDataReader reader, int ordinal)
	{
		value = reader.GetString(ordinal);
	}

	public void Serialize(Response row)
	{
		row.WriteString(value);
	}

	public string GetDefaultTypeName() => "VARCHAR2";
}