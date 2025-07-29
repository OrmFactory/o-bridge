using System.Data.Common;
using OBridge.Server.ValueObjects;

namespace OBridge.Server;

public class Column
{
	private readonly DbColumn column;
	private readonly int ordinal;
	private readonly byte fieldPresenceMask = 0;

	public readonly IValueObject ValueObject;
	public int Ordinal => ordinal;

	public bool IsNullable => column.AllowDBNull ?? true;

	public bool IsFieldPresent(int bit)
	{
		return (fieldPresenceMask & (1 << bit)) != 0;
	}

	public Column(DbColumn column)
	{
		this.column = column;
		this.ordinal = column.ColumnOrdinal ?? throw new Exception();
		fieldPresenceMask = GetFieldPresenceMask();
		ValueObject = CreateValueObject();
	}

	public void WriteHeader(Response response)
	{
		response.WriteByte(fieldPresenceMask);
		response.WriteString(column.ColumnName ?? "");

		if (IsFieldPresent(0)) response.WriteByte(column.AllowDBNull!.Value ? (byte)1 : (byte)0);
		if (IsFieldPresent(1)) response.Write7BitEncodedInt(column.ColumnSize!.Value);
		if (IsFieldPresent(2)) response.WriteByte((byte)column.NumericPrecision!.Value);
		if (IsFieldPresent(3)) response.WriteByte((sbyte)column.NumericScale!.Value);
		if (IsFieldPresent(4)) response.WriteByte(column.IsAliased!.Value ? (byte)1 : (byte)0);
		if (IsFieldPresent(5)) response.WriteByte(column.IsExpression!.Value ? (byte)1 : (byte)0);
		if (IsFieldPresent(6)) response.WriteString(column.BaseColumnName ?? "");
		if (IsFieldPresent(7)) response.WriteString(column.BaseTableName ?? "");
	}

	private byte GetFieldPresenceMask()
	{
		byte nullFlags = 0;

		if (column.AllowDBNull.HasValue) nullFlags |= 1 << 0;
		if (column.ColumnSize.HasValue) nullFlags |= 1 << 1;
		if (column.NumericPrecision.HasValue) nullFlags |= 1 << 2;
		if (column.NumericScale.HasValue) nullFlags |= 1 << 3;
		if (column.IsAliased.HasValue) nullFlags |= 1 << 4;
		if (column.IsExpression.HasValue) nullFlags |= 1 << 5;
		if (!string.IsNullOrEmpty(column.BaseColumnName)) nullFlags |= 1 << 6;
		if (!string.IsNullOrEmpty(column.BaseTableName)) nullFlags |= 1 << 7;

		return nullFlags;
	}

	private IValueObject CreateValueObject()
	{
		var type = column.DataType;

		var dataType = column.DataTypeName?.ToLower() ?? "";

		if (dataType.StartsWith("number")) return new NumberValue();
		if (dataType == "date") return new DateTimeValue(column.NumericScale ?? 0, false);
		if (dataType.StartsWith("timestamp"))
		{
			if (dataType == "timestamp with time zone") return new DateTimeValue(column.NumericScale ?? 0, true);
			return new DateTimeValue(column.NumericScale ?? 0, false);
		}

		if (dataType == "interval year to month") return new IntervalYearToMonth();
		if (dataType == "interval day to second") return new IntervalDayToSecond(column.NumericScale ?? 0);

		if (type == typeof(bool)) return new BooleanValue();
		if (type == typeof(float)) return new FloatValue();
		if (type == typeof(double)) return new DoubleValue();
		if (type == typeof(Guid)) return new GuidValue();
		if (type == typeof(string)) return new StringValue();
		if (type == typeof(byte[])) return new BinaryValue();
		return new StringValue();
	}
}