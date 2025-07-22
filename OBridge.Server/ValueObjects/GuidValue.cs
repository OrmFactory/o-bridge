using Oracle.ManagedDataAccess.Client;

namespace OBridge.Server.ValueObjects;

public class GuidValue : IValueObject
{
	private Guid value;

	public void LoadFromReader(OracleDataReader reader, int ordinal)
	{
		value = reader.GetGuid(ordinal);
	}

	public void Serialize(Response row)
	{
		row.WriteBytes(value.ToByteArray());
	}
}