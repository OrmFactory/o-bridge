using Oracle.ManagedDataAccess.Client;
using System.Buffers.Binary;

namespace OBridge.Server.ValueObjects;

public class DateTimeValue : IValueObject
{
	private readonly int secondsPrecision;
	private readonly bool containsTimeZone;
	private int year;
	private int month;
	private int day;
	private int hour;
	private int minute;
	private int second;
	private int nanosecond;
	
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
			year = tz.Year;
			month = tz.Month;
			day = tz.Day;
			hour = tz.Hour;
			minute = tz.Minute;
			second = tz.Second;
			nanosecond = tz.Nanosecond;
			return;
		}

		var ts = reader.GetOracleTimeStamp(ordinal);
		year = ts.Year;
		month = ts.Month;
		day = ts.Day;
		hour = ts.Hour;
		minute = ts.Minute;
		second = ts.Second;
		nanosecond = ts.Nanosecond;
	}

	public void Serialize(Response row)
	{
		var hasFraction = secondsPrecision > 0 && nanosecond != 0;
		var hasTimezone = containsTimeZone && timeZoneOffsetMinutes != 0;
		var isDateOnly = hour == 0 && minute == 0 && second == 0 && !hasTimezone && !hasFraction;

		// Header bits
		uint dateOnlyValue = 0;
		if (isDateOnly) dateOnlyValue |= 1U << 0;
		if (hasFraction) dateOnlyValue |= 1U << 1;
		if (hasTimezone) dateOnlyValue |= 1U << 2;

		//reserved 4 bits

		// Year: 1 sign bit + 14-bit abs
		uint yearBits = (uint)(Math.Abs(year) & 0x3FFF);
		if (year < 0) yearBits |= 1U << 14;
		dateOnlyValue |= (yearBits & 0x7FFF) << 7;

		dateOnlyValue |= (uint)(month & 0xF) << 22;
		dateOnlyValue |= (uint)(day & 0x1F) << 26;

		if (isDateOnly)
		{
			row.WriteUInt32(dateOnlyValue);
			return;
		}

		ulong value = dateOnlyValue;

		value |= (ulong)(hour & 0x1F) << 31;
		value |= (ulong)(minute & 0x3F) << 36;
		value |= (ulong)(second & 0x3F) << 41;

		uint low = (uint)(value & 0xFFFF_FFFF);
		ushort high = (ushort)((value >> 32) & 0xFFFF);
		row.WriteUInt32(low);
		row.WriteUInt16(high);

		if (hasFraction)
		{
			int scale = 9 - secondsPrecision;
			int scaled = nanosecond;
			if (scale > 0) scaled /= PowersOf10[scale];

			if (secondsPrecision <= 2)
			{
				// 1 byte
				row.WriteByte((byte)scaled);
			}
			else if (secondsPrecision <= 4)
			{
				// 2 bytes
				row.WriteUInt16((ushort)scaled);
			}
			else if (secondsPrecision <= 6)
			{
				// 3 bytes
				row.WriteBytes((byte)(scaled >> 16), (byte)(scaled >> 8), (byte)scaled);
			}
			else
			{
				// 4 bytes
				row.WriteUInt32((uint)scaled);
			}
		}

		if (hasTimezone)
		{
			row.WriteInt16(timeZoneOffsetMinutes);
		}
	}

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
}