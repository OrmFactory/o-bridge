using System.Net.Sockets;
using System.Text;


var client = new TcpClient("localhost", 0x0fac);
using var stream = client.GetStream();

var sql = "SELECT * FROM dual";
var payload = Encoding.UTF8.GetBytes(sql);
int length = payload.Length + 1;

await stream.WriteAsync(BitConverter.GetBytes(length));
await stream.WriteAsync([0x01], 0, 1);
await stream.WriteAsync(payload);

var header = await ReadExactAsync(stream, 4);
int len = BitConverter.ToInt32(header);
byte type = (byte)stream.ReadByte();
var data = await ReadExactAsync(stream, len - 1);

Console.WriteLine($"[Response Type={type}] {Encoding.UTF8.GetString(data)}");

static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int count)
{
	var buffer = new byte[count];
	int offset = 0;
	while (offset < count)
	{
		int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
		if (read == 0) return null;
		offset += read;
	}
	return buffer;
}