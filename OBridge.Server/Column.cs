using System.Data.Common;
using OBridge.Server.ValueObjects;

namespace OBridge.Server;

public class Column
{
	private readonly DbColumn column;
	private readonly int ordinal;
	private readonly byte fieldPresenceMask = 0;
	private readonly TypeCodeEnum fieldTypeCode;

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
		fieldTypeCode = GetTypeCode();
	}

	public void WriteHeader(Response response)
	{
		response.WriteByte(fieldPresenceMask);
		response.WriteString(column.ColumnName ?? "");
		response.WriteByte((byte)fieldTypeCode);

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

	private TypeCodeEnum GetTypeCode()
	{
		var type = column.DataType;

		var dataType = column.DataTypeName?.ToLower() ?? "";

		if (dataType.StartsWith("number")) return TypeCodeEnum.Number;
		if (dataType == "date") return TypeCodeEnum.DateTime;
		if (dataType.StartsWith("timestamp"))
		{
			if (dataType == "timestamp with time zone") return TypeCodeEnum.DateTimeTz;
			return TypeCodeEnum.DateTime;
		}

		if (dataType == "interval year to month") return TypeCodeEnum.IntervalYearToMonth;
		if (dataType == "interval day to second") return TypeCodeEnum.IntervalDayToSecond;

		if (type == typeof(bool)) return TypeCodeEnum.Boolean;
		if (type == typeof(float)) return TypeCodeEnum.Float;
		if (type == typeof(double)) return TypeCodeEnum.Double;
		if (type == typeof(Guid)) return TypeCodeEnum.Guid;
		if (type == typeof(string)) return TypeCodeEnum.String;
		if (type == typeof(byte[])) return TypeCodeEnum.Binary;
		return TypeCodeEnum.String;
	}

	public IValueObject GetValueObject()
	{
		return fieldTypeCode switch
		{
			TypeCodeEnum.Boolean => new BooleanValue(),
			TypeCodeEnum.Float => new FloatValue(),
			TypeCodeEnum.Double => new DoubleValue(),
			TypeCodeEnum.Number => new NumberValue(),
			TypeCodeEnum.DateTime => new DateTimeValue(column.NumericScale ?? 0, false),
			TypeCodeEnum.DateTimeTz => new DateTimeValue(column.NumericScale ?? 0, true),
			TypeCodeEnum.IntervalDayToSecond => new IntervalDayToSecond(column.NumericScale ?? 0),
			TypeCodeEnum.IntervalYearToMonth => new IntervalYearToMonth(),
			TypeCodeEnum.Guid => new GuidValue(),
			TypeCodeEnum.String => new StringValue(),
			_ => throw new NotImplementedException()
		};
	}
}