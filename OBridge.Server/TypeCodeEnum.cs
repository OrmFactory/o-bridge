namespace OBridge.Server;

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