using System.ComponentModel.Design;
using Oracle.ManagedDataAccess.Client;

namespace OBridge.Server.ValueObjects;

public interface IValueObject
{
	void LoadFromReader(OracleDataReader reader, int ordinal);
	void Serialize(Response row);
}