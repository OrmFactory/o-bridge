using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;

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
			using var cmd = connection.CreateCommand();
			cmd.CommandText = query;
			cmd.CommandType = System.Data.CommandType.Text;

			using var reader = await cmd.ExecuteReaderAsync(stopQueryToken);
			int fieldCount = reader.FieldCount;

			for (int i = 0; i < fieldCount; i++)
			{
				//writter.WriteString(reader.GetName(i));
			}


		}
		catch (OracleException ex)
		{
			var error = new Response(ResponseTypeEnum.OracleQueryError);
			error.WriteString(ex.Message);
			await error.SendAsync(stream, token);
		}
		finally
		{
			queryCts?.Dispose();
			queryCts = null;
			queryTask = null;
		}
	}
}