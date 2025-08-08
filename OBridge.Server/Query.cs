using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using System;
using System.Collections.Generic;
using System.Data;
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
	private CommandBehavior commandBehavior = CommandBehavior.Default;
	private List<OracleParameter> parameters;

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
		byte behavior = await reader.ReadByte().ConfigureAwait(false);

		//remove SequentialAccess flag
		commandBehavior = (CommandBehavior)(behavior & 31);
		reader.MaxStringBytes = 1024 * 1024;
		query = await reader.ReadString().ConfigureAwait(false);
		await ReadParameters(reader).ConfigureAwait(false);
	}

	private async Task ReadParameters(AsyncBinaryReader reader)
	{
		parameters = new();
		var parametersCount = await reader.Read7BitEncodedInt().ConfigureAwait(false);
		
		for (int i = 0; i < parametersCount; i++)
		{
			string name = await reader.ReadString().ConfigureAwait(false);
			var dbType = (OracleDbType)await reader.ReadByte().ConfigureAwait(false);
			var direction = (ParameterDirection)await reader.ReadByte().ConfigureAwait(false);

			var p = new OracleParameter
			{
				ParameterName = name,
				Direction = direction,
				OracleDbType = dbType
			};
			parameters.Add(p);

			var isNull = await reader.ReadBoolean().ConfigureAwait(false);
			p.Value = isNull
				? DBNull.Value
				: await ReadValue(reader, dbType).ConfigureAwait(false);
		}
	}

	private async Task<object?> ReadValue(AsyncBinaryReader reader, OracleDbType type)
	{
		switch (type)
		{
			case OracleDbType.Int16:
				return await reader.ReadInt16().ConfigureAwait(false);

			case OracleDbType.Int32:
				return await reader.ReadInt32().ConfigureAwait(false);

			case OracleDbType.Int64:
				return await reader.ReadInt64().ConfigureAwait(false);

			case OracleDbType.Single:
			case OracleDbType.BinaryFloat:
				return await reader.ReadFloat().ConfigureAwait(false);

			case OracleDbType.Double:
			case OracleDbType.BinaryDouble:
				return await reader.ReadDouble().ConfigureAwait(false);

			case OracleDbType.Decimal:
				return await reader.ReadDecimal().ConfigureAwait(false);

			case OracleDbType.Boolean:
				return await reader.ReadBoolean().ConfigureAwait(false);

			case OracleDbType.Date:
				return await reader.ReadDateTime().ConfigureAwait(false);

			case OracleDbType.TimeStamp:
			case OracleDbType.TimeStampLTZ:
			case OracleDbType.TimeStampTZ:
				throw new NotImplementedException(type.ToString());

			case OracleDbType.Char:
			case OracleDbType.NChar:
			case OracleDbType.Varchar2:
			case OracleDbType.NVarchar2:
			case OracleDbType.Clob:
			case OracleDbType.NClob:
			case OracleDbType.Json:
			case OracleDbType.ArrayAsJson:
			case OracleDbType.ObjectAsJson:
				return await reader.ReadString().ConfigureAwait(false);

			case OracleDbType.Raw:
			case OracleDbType.LongRaw:
			case OracleDbType.Blob:
				return await reader.ReadBinary().ConfigureAwait(false);

			default:
				throw new NotSupportedException($"Unsupported OracleDbType: {type}");
		}
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
			cmd.CommandType = CommandType.Text;
			//
			cmd.InitialLONGFetchSize = -1;

			await using var reader = await cmd.ExecuteReaderAsync(commandBehavior, stopQueryToken);
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
						var val = column.ValueObject;
						val.LoadFromReader(reader, column.Ordinal);
						val.Serialize(row);
					}
				}

				await row.SendAsync(stream, stopQueryToken);
			}

			await reader.CloseAsync();
			var endOfStream = new Response(ResponseTypeEnum.EndOfRowStream);
			endOfStream.Write7BitEncodedInt(reader.RecordsAffected);
			WriteOutputParameters(endOfStream);
			await endOfStream.SendAsync(stream, stopQueryToken);
			await stream.FlushAsync(stopQueryToken);
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

	private void WriteOutputParameters(Response response)
	{
		var outputParameters = parameters
			.Where(p => p.Direction == ParameterDirection.Output || p.Direction == ParameterDirection.ReturnValue)
			.ToList();

		response.Write7BitEncodedInt(outputParameters.Count);
		foreach (var p in outputParameters)
		{
			response.WriteString(p.ParameterName);
			response.WriteByte((byte)p.OracleDbType);
			response.WriteByte((byte)p.Direction);
			var isNull = p.Value == null || p.Value == DBNull.Value;
			response.WriteBoolean(isNull);
			if (!isNull) SerializeValue(response, p);
		}
	}

	private void SerializeValue(Response response, OracleParameter parameter)
	{
		var value = parameter.Value;
		var type = parameter.OracleDbType;

		switch (type)
		{
			case OracleDbType.Int16:
				response.WriteInt16(Convert.ToInt16(value));
				break;
			case OracleDbType.Int32:
				response.WriteInt32(Convert.ToInt32(value));
				break;
			case OracleDbType.Int64:
				response.WriteInt64(Convert.ToInt64(value));
				break;
			case OracleDbType.Single:
			case OracleDbType.BinaryFloat:
				response.WriteFloat(Convert.ToSingle(value));
				break;
			case OracleDbType.Double:
			case OracleDbType.BinaryDouble:
				response.WriteDouble(Convert.ToDouble(value));
				break;
			case OracleDbType.Decimal:
				response.WriteDecimal(Convert.ToDecimal(value));
				break;
			case OracleDbType.Boolean:
				response.WriteBoolean(Convert.ToBoolean(value));
				break;
			case OracleDbType.Date:
				response.WriteDateTime(Convert.ToDateTime(value));
				break;
			case OracleDbType.TimeStamp:
			case OracleDbType.TimeStampLTZ:
			case OracleDbType.TimeStampTZ:
				throw new NotImplementedException(type.ToString());
			case OracleDbType.Char:
			case OracleDbType.NChar:
			case OracleDbType.Varchar2:
			case OracleDbType.NVarchar2:
			case OracleDbType.Clob:
			case OracleDbType.NClob:
			case OracleDbType.Json:
				response.WriteString(Convert.ToString(value));
				break;
			case OracleDbType.Raw:
			case OracleDbType.LongRaw:
			case OracleDbType.Blob:
				response.WriteBytes((byte[])value);
				break;
			default:
				throw new NotSupportedException($"Unsupported OracleDbType: {type}");
		}
	}
}