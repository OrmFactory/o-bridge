using Oracle.ManagedDataAccess.Client;

namespace OBridge.Server.ValueObjects;

public class BinaryValue : IValueObject
{
	private byte[] bytes;

	public void LoadFromReader(OracleDataReader reader, int ordinal)
	{
		bytes = reader.GetByteArray(ordinal);
	}

	public void Serialize(Response row)
	{
		row.WriteBytes(bytes);
	}

	public string GetDefaultTypeName() => "RAW";
}