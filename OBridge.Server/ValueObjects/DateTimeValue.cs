using Oracle.ManagedDataAccess.Client;
using System.Buffers.Binary;
using System.Data;

namespace OBridge.Server.ValueObjects;

public class DateTimeValue : IValueObject
{
	private readonly int secondsPrecision;
	private readonly DateTimeFormatEnum dateTimeFormat;
	private int year;
	private int month;
	private int day;
	private int hour;
	private int minute;
	private int second;
	private int nanosecond;
	private short timeZoneOffsetMinutes;

	public DateTimeValue(int secondsPrecision, DateTimeFormatEnum format)
	{
		this.secondsPrecision = secondsPrecision;
		this.dateTimeFormat = format;
	}

	public void LoadFromReader(OracleDataReader reader, int ordinal)
	{
		if (dateTimeFormat == DateTimeFormatEnum.TimestampWithTimeZone)
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

		if (dateTimeFormat == DateTimeFormatEnum.Date)
		{
			var dt = reader.GetDateTime(ordinal);
			year = dt.Year;
			month = dt.Month;
			day = dt.Day;
			hour = dt.Hour;
			minute = dt.Minute;
			second = dt.Second;
			nanosecond = 0;
			timeZoneOffsetMinutes = 0;
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
		timeZoneOffsetMinutes = 0;
	}

	public void Serialize(Response row)
	{
		var hasFraction = secondsPrecision > 0 && nanosecond != 0;
		var hasTimezone = dateTimeFormat == DateTimeFormatEnum.TimestampWithTimeZone && timeZoneOffsetMinutes != 0;
		var isDateOnly = hour == 0 && minute == 0 && second == 0 && !hasTimezone && !hasFraction;

		var writer = new BitWriter();

		// --- Header bits ---
		writer.AddBit(isDateOnly);
		writer.AddBit(hasFraction);
		writer.AddBit(hasTimezone);

		// --- Date ---
		int absYear = Math.Abs(year);
		writer.AddBit(year < 0);
		writer.AddBits(absYear, 14);
		writer.AddBits(month, 4);
		writer.AddBits(day, 5);

		if (isDateOnly)
		{
			row.WriteBytes(writer.ToArray());
			return;
		}

		// --- Time ---
		writer.AddBits(hour, 5);
		writer.AddBits(minute, 6);
		writer.AddBits(second, 6);

		if (hasFraction)
		{
			int scale = 9 - secondsPrecision;
			int scaled = nanosecond;
			if (scale > 0) scaled /= PowersOf10[scale];

			int totalBits = FractionBitLengths[secondsPrecision];
			int highBits = Math.Min(4, totalBits);
			int lowBits = totalBits - highBits;

			writer.AddBits(scaled >> lowBits, highBits);

			if (lowBits > 0)
			{
				int lowValue = scaled & ((1 << lowBits) - 1);
				writer.AddBits(lowValue, lowBits);
			}
		}

		if (hasTimezone)
		{
			writer.AddBit(timeZoneOffsetMinutes < 0);
			writer.AddBits(Math.Abs(timeZoneOffsetMinutes), 10);
		}

		row.WriteBytes(writer.ToArray());
	}

	public string GetDefaultTypeName()
	{
		if (dateTimeFormat == DateTimeFormatEnum.Date) return "DATE";
		if (dateTimeFormat == DateTimeFormatEnum.TimestampWithTimeZone) return "TIMESTAMP WITH TIME ZONE";
		if (dateTimeFormat == DateTimeFormatEnum.TimestampWithLocalTimeZone) return "TIMESTAMP WITH LOCAL TIME ZONE";
		throw new Exception();
	}

	private static readonly int[] FractionBitLengths = new int[]
	{
		0,  // precision 0
		4,  // precision 1 (0..9)
		7,  // precision 2 (0..99)
		10, // precision 3 (0..999)
		14, // precision 4
		17, // precision 5
		20, // precision 6
		24, // precision 7
		27, // precision 8
		30  // precision 9
	};

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

public enum DateTimeFormatEnum
{
	Date,
	TimestampWithTimeZone,
	TimestampWithLocalTimeZone
}
