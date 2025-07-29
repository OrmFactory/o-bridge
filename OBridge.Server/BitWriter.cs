using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OBridge.Server;

public class BitWriter
{
	private readonly List<byte> buffer = new();
	private int currentByte = 0;
	private int bitPosition = 0; // from 0 (MSB) to 7 (LSB)

	public void AddBit(bool value)
	{
		if (value)
			currentByte |= 1 << (7 - bitPosition);

		bitPosition++;

		if (bitPosition == 8)
		{
			FlushCurrentByte();
		}
	}

	public void AddBits(int value, int bitCount)
	{

		while (bitCount > 0)
		{
			int bitsAvailable = 8 - bitPosition;
			int bitsToWrite = Math.Min(bitCount, bitsAvailable);

			int shift = bitCount - bitsToWrite;
			int mask = (1 << bitsToWrite) - 1;
			int bits = (int)((value >> shift) & mask);

			currentByte |= bits << (bitsAvailable - bitsToWrite);
			bitPosition += bitsToWrite;
			bitCount -= bitsToWrite;

			if (bitPosition == 8)
				FlushCurrentByte();
		}
	}

	public byte[] ToArray()
	{
		if (bitPosition > 0)
			FlushCurrentByte();

		return buffer.ToArray();
	}

	private void FlushCurrentByte()
	{
		buffer.Add((byte)currentByte);
		currentByte = 0;
		bitPosition = 0;
	}
}
