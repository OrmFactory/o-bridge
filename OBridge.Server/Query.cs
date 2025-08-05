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
		byte behavior = await reader.ReadByteAsync();

		//remove SequentialAccess flag
		commandBehavior = (CommandBehavior)(behavior & 31);

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
					if (reader.IsDBNull(nullableColumn.Ordinal)) presenceMaskByte |= bitMask;
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
}