using Oracle.ManagedDataAccess.Client;

namespace OBridge.Server.ValueObjects;

public class IntervalYearToMonth : IValueObject
{
	public int Years;
	public int Months;
	public bool IsNegative;

	public void LoadFromReader(OracleDataReader reader, int ordinal)
	{
		var interval = reader.GetOracleIntervalYM(ordinal);
		IsNegative = interval.TotalYears < 0;

		Years =  Math.Abs(interval.Years);
		Months = Math.Abs(interval.Months);
	}

	public void Serialize(Response response)
	{
		byte meta = 0;
		if (Years != 0) meta |= 0x01;
		if (Months != 0) meta |= 0x02;
		if (IsNegative) meta |= 0x80;

		response.WriteByte(meta);

		if (Years != 0) response.Write7BitEncodedInt(Years);
		if (Months != 0) response.Write7BitEncodedInt(Months);
	}
}