using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace OBridge.Server.ValueObjects;

public class IntervalDayToSecond : IValueObject
{
	public readonly int SecondPrecision;

	public int Days;
	public int Hours;
	public int Minutes;
	public int Seconds;
	public int FractionalSeconds;
	public bool IsNegative;

	public IntervalDayToSecond(int secondPrecision)
	{
		Days = 0;
		Hours = 0;
		Minutes = 0;
		Seconds = 0;
		FractionalSeconds = 0;
		SecondPrecision = secondPrecision;
		IsNegative = false;
	}
	
	public void LoadFromReader(OracleDataReader reader, int ordinal)
	{
		var interval = reader.GetOracleIntervalDS(ordinal);
		IsNegative = interval.CompareTo(OracleIntervalDS.Zero) < 0;
		var nanoseconds = Math.Abs(interval.Nanoseconds);
		FractionalSeconds = RoundFractionalSeconds(nanoseconds, SecondPrecision);

		Days = Math.Abs(interval.Days);
		Hours = Math.Abs(interval.Hours);
		Minutes = Math.Abs(interval.Minutes);
		Seconds = Math.Abs(interval.Seconds);
	}

	public void Serialize(Response response)
	{
		byte meta = 0;
		if (Days != 0) meta |= 0x01;
		if (Hours != 0) meta |= 0x02;
		if (Minutes != 0) meta |= 0x04;
		if (Seconds != 0) meta |= 0x08;
		if (FractionalSeconds != 0) meta |= 0x10;
		if (IsNegative) meta |= 0x80;

		response.WriteByte(meta);
		if (Days != 0) response.Write7BitEncodedInt(Days);
		if (Hours != 0) response.Write7BitEncodedInt(Hours);
		if (Minutes != 0) response.Write7BitEncodedInt(Minutes);
		if (Seconds != 0) response.Write7BitEncodedInt(Seconds);
		if (SecondPrecision > 0 && FractionalSeconds != 0) response.Write7BitEncodedInt(FractionalSeconds);
	}

	public string GetDefaultTypeName() => "INTERVAL DAY TO SECOND";

	private static readonly int[] PowersOf10 = new int[]
	{
		1,              // 10^0
		10,             // 10^1
		100,            // 10^2
		1_000,          // 10^3
		10_000,         // 10^4
		100_000,        // 10^5
		1_000_000,      // 10^6
		10_000_000,     // 10^7
		100_000_000,    // 10^8
		1_000_000_000   // 10^9
	};

	private static int RoundFractionalSeconds(int nanoseconds, int precision)
	{
		if (precision == 0 || nanoseconds == 0)
			return 0;

		if (precision > 9) precision = 9;

		var scale = PowersOf10[9 - precision];
		return nanoseconds / scale;
	}
}