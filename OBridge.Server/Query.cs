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
	private readonly BinaryWriter writter;
	private readonly CancellationToken token;
	private string query;

	private CancellationTokenSource? queryCts;
	private Task? queryTask;

	public Query(OracleConnection connection, BinaryWriter writter, CancellationToken token)
	{
		this.connection = connection;
		this.writter = writter;
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

		}
		catch (OperationCanceledException e)
		{
			writter.Write((byte)0x10);
			writter.Write((byte)ErrorCode.QueryCancelledByClient);
			writter.Write(e.Message);
			writter.Flush();
		}
		finally
		{
			queryCts?.Dispose();
			queryCts = null;
			queryTask = null;
		}
	}
}