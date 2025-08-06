using System.Data.Common;
using OBridge.Server.ValueObjects;

namespace OBridge.Server;

public class Column
{
	private readonly DbColumn column;
	private readonly byte fieldPresenceMask = 0;

	public readonly IValueObject ValueObject;
	public int Ordinal => column.ColumnOrdinal ?? throw new Exception();
	public bool IsNullable => column.AllowDBNull ?? true;

	private readonly string dataTypeName;

	public bool IsFieldPresent(int bit)
	{
		return (fieldPresenceMask & (1 << bit)) != 0;
	}

	public Column(DbColumn column)
	{
		this.column = column;
		fieldPresenceMask = GetFieldPresenceMask();
		if (column.DataTypeName != null)
		{
			ValueObject = CreateValueObjectFromDataTypeName();
			dataTypeName = column.DataTypeName;
		}
		else
		{
			ValueObject = CreateValueFromDataType();
			dataTypeName = ValueObject.GetDefaultTypeName();
		}
	}

	public void WriteHeader(Response response)
	{
		response.WriteByte(fieldPresenceMask);
		response.WriteString(column.ColumnName);
		response.WriteString(dataTypeName);

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

	private IValueObject CreateValueObjectFromDataTypeName()
	{
		var dataType = column.DataTypeName?.ToLower() ?? "";

		if (dataType.StartsWith("number")) return new NumberValue();
		if (dataType == "date") return new DateTimeValue(column.NumericScale ?? 0, DateTimeFormatEnum.Date);
		if (dataType.StartsWith("timestamp"))
		{
			if (dataType == "timestamp with time zone") return new DateTimeValue(column.NumericScale ?? 0, DateTimeFormatEnum.TimestampWithTimeZone);
			return new DateTimeValue(column.NumericScale ?? 0, DateTimeFormatEnum.TimestampWithLocalTimeZone);
		}

		if (dataType == "interval year to month") return new IntervalYearToMonth();
		if (dataType == "interval day to second") return new IntervalDayToSecond(column.NumericScale ?? 0);
		if (dataType is "char" or "nchar" or "varchar2" or "nvarchar2" or "clob" or "nclob") return new StringValue();
		if (dataType is "raw" or "long raw" or "blob" or "bfile") return new BinaryValue();
		if (dataType == "boolean") return new BooleanValue();
		if (dataType == "binary_float") return new FloatValue();
		if (dataType == "binary_double") return new DoubleValue();
		return new StringValue();
	}

	private IValueObject CreateValueFromDataType()
	{
		var dt = column.DataType;
		if (dt == null) throw new Exception("DataType is null");

		if (dt == typeof(string)) return new StringValue();
		if (dt == typeof(decimal)) return new NumberValue();
		if (dt == typeof(long)) return new NumberValue();
		if (dt == typeof(int)) return new NumberValue();
		if (dt == typeof(short)) return new NumberValue();
		if (dt == typeof(double)) return new DoubleValue();
		if (dt == typeof(float)) return new FloatValue();
		if (dt == typeof(byte[])) return new BinaryValue();
		if (dt == typeof(DateTimeOffset)) return new DateTimeValue(column.NumericScale ?? 0, DateTimeFormatEnum.TimestampWithTimeZone);
		if (dt == typeof(DateTime)) return new DateTimeValue(column.NumericScale ?? 0, DateTimeFormatEnum.Date);

		throw new NotImplementedException("Not implemented fallback for type '" + dt.FullName + "'");
	}
}