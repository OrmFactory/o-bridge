using Oracle.ManagedDataAccess.Client;

namespace OBridge.Server.ValueObjects;

public class DateTimeValue : IValueObject
{
	private readonly int secondsPrecision;
	private readonly bool containsTimeZone;
	private DateTime value;
	private byte nanosecondsRemains;
	private short timeZoneOffsetMinutes;

	public DateTimeValue(int secondsPrecision, bool containsTimeZone)
	{
		this.secondsPrecision = secondsPrecision;
		this.containsTimeZone = containsTimeZone;
	}

	public void LoadFromReader(OracleDataReader reader, int ordinal)
	{
		if (containsTimeZone)
		{
			var tz = reader.GetOracleTimeStampTZ(ordinal);
			timeZoneOffsetMinutes = (short)tz.GetTimeZoneOffset().TotalMinutes;
			value = tz.Value;
			nanosecondsRemains = (byte)(tz.Nanosecond % 100);
			return;
		}

		if (secondsPrecision > 7)
		{
			var tz = reader.GetOracleTimeStamp(ordinal);
			value = tz.Value;
			nanosecondsRemains = (byte)(tz.Nanosecond % 100);
			return;
		}

		value = reader.GetDateTime(ordinal);
	}

	public void Serialize(Response row)
	{
		row.WriteInt64(value.ToBinary());
		if (secondsPrecision > 7) row.WriteByte(nanosecondsRemains);
		if (containsTimeZone) row.WriteInt16(timeZoneOffsetMinutes);
	}
}