using OBridge.Server.ValueObjects;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OBridge.Server;

public class Query
{
	private readonly OracleConnection connection;
	private readonly Stream stream;
	private readonly CancellationToken token;
	private string query;

	private CancellationTokenSource? queryCts;
	private Task? queryTask;

	public Query(OracleConnection connection, Stream stream, CancellationToken token)
	{
		this.connection = connection;
		this.stream = stream;
		this.token = token;
	}

	public async Task ReadQuery(AsyncBinaryReader reader)
	{
		reader.MaxStringBytes = 1024 * 1024;
		query = await reader.ReadStringAsync();
	}

	public async Task Stop()
	{
		if (queryCts != null && !queryCts.IsCancellationRequested) await queryCts.CancelAsync();
		if (queryTask != null) await queryTask;
	}

	public async Task Finish()
	{
		if (queryTask != null) await queryTask;
	}

	public Task Execute()
	{
		queryCts = CancellationTokenSource.CreateLinkedTokenSource(token);
		queryTask = Task.Run(() => Execute(queryCts.Token), queryCts.Token);
		return queryTask;
	}

	private async Task Execute(CancellationToken stopQueryToken)
	{
		try
		{
			await using var cmd = connection.CreateCommand();
			cmd.CommandText = query;
			cmd.CommandType = System.Data.CommandType.Text;

			await using var reader = await cmd.ExecuteReaderAsync(stopQueryToken);
			var schema = await reader.GetColumnSchemaAsync(stopQueryToken);
			var columnCount = schema.Count;
			var columnsHeader = new Response(ResponseTypeEnum.TableHeader);
			columnsHeader.Write7BitEncodedInt(columnCount);

			var columns = new List<Column>();
			for (int i = 0; i < columnCount; i++)
			{
				var column = new Column(schema[i]);
				column.WriteHeader(columnsHeader);
				columns.Add(column);
			}
			await columnsHeader.SendAsync(stream, stopQueryToken);

			var nullableColumns = columns.Where(c => c.IsNullable).ToList();

			while (await reader.ReadAsync(stopQueryToken))
			{
				var row = new Response(ResponseTypeEnum.RowData);
				byte bitMask = 1;
				byte presenceMaskByte = 0;
				foreach (var nullableColumn in nullableColumns)
				{
					if (!reader.IsDBNull(nullableColumn.Ordinal)) presenceMaskByte |= bitMask;
					if (bitMask == 128)
					{
						bitMask = 1;
						row.WriteByte(presenceMaskByte);
						presenceMaskByte = 0;
					}
					else bitMask *= 2;
				}
				if (bitMask != 1) row.WriteByte(presenceMaskByte);
				foreach (var column in columns)
				{
					if (!reader.IsDBNull(column.Ordinal))
					{
						var val = column.GetValueObject();
						val.LoadFromReader(reader, column.Ordinal);
						val.Serialize(row);
					}
				}
			}

		}
		catch (OracleException ex)
		{
			var error = new Response(ResponseTypeEnum.OracleQueryError);
			error.WriteString(ex.Message);
			await error.SendAsync(stream, token);
			await stream.FlushAsync(token);
		}
		finally
		{
			queryCts?.Dispose();
			queryCts = null;
			queryTask = null;
		}
	}
}

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

public enum TypeCodeEnum
{
	Boolean = 0x01,
	Float = 0x03,
	Double = 0x04,
	DateTime = 0x05,
	DateTimeTz = 0x06,
	IntervalDayToSecond = 0x07,
	IntervalYearToMonth = 0x08,
	Guid = 0x09,
	String = 0x10,
	Binary = 0x11,
	Number = 0x20,
}