# `o-Connector`: Type Serialization Format

This document describes **binary serialization rules** for all supported value types in the o-Connector protocol. It is intended for both client and server implementations that serialize/deserialize tabular data and parameters.
## Assumptions

- **Type and nullability** are always known (defined in headers).  
- Null values are not serialized — tracked via separate bitmaps.
- All values are written in **binary, unambiguous format**, optimized for streaming.
## Fixed-size Types

| Type       | Code | Size     | Format Description                           |
| ---------- | ---- | -------- | -------------------------------------------- |
| `Boolean`  | 0x01 | 1 byte   | `0x00` = false, `0x01` = true                |
| `Int32`    | 0x02 | 4 bytes  | Little-endian signed 32-bit integer          |
| `Int64`    | 0x03 | 8 bytes  | Little-endian signed 64-bit integer          |
| `Float32`  | 0x04 | 4 bytes  | IEEE 754 binary32                            |
| `Float64`  | 0x05 | 8 bytes  | IEEE 754 binary64                            |
| `DateTime` | 0x06 | 8 bytes  | .NET `Ticks` (Int64, 100ns since 0001-01-01) |
| `Guid`     | 0x07 | 16 bytes | Raw 128-bit GUID (standard layout)           |
## Variable-size Types

### `String` (0x10)

```
[int32 Length][UTF-8 bytes]
```

- Length in bytes (not characters).
- Encoding is UTF-8 without BOM.
- No null-terminator.
### `Binary` (0x11)

```
[int32 Length][Raw Bytes]
```

- Used for Oracle `RAW`, `BLOB`, etc.
- Uncompressed, raw byte stream.
## Decimal / Oracle NUMBER (0x20)

Binary serialization based on Oracle's flexible precision decimal format.
### Format A: Single-byte form (unsigned integer 0–127)

```
[Meta: byte]    // (Meta & 0x80) == 0x80
```

Meta byte: highest bit `1`, remaining 7 bits = integer value.
## Format B: Compact decimal (BCD)

```
[Meta: byte]           // (Meta & 0x80) == 0
[Scale: int8]          // decimal scale
[Digits: bytes]        // BCD digits (two per byte)
```

- `Meta`:
    - Bit 7 (0x80): must be 0 (not unsigned integer)
    - Bit 6 (0x40): **sign** (0 = positive, 1 = negative)
    - Bits 0–5: number of decimal digits (0–63)
- `Scale`: signed byte; e.g. `-2` = ÷100, `3` = ×1000
- `Digits`: ceil(Digits / 2) bytes, big-endian BCD (high nibble first)

**Examples:**

- Value: `42` → Meta `0x82`
- Value: `-123.45` → Meta `0xC5`, Scale `-2`, Digits: `0x12 0x34 0x50`

## Summary Table

| Type Name | Code | Size | Description                       |
| --------- | ---- | ---- | --------------------------------- |
| Boolean   | 0x01 | 1    | `0x00` or `0x01`                  |
| Int32     | 0x02 | 4    | little-endian                     |
| Int64     | 0x03 | 8    | little-endian                     |
| Float32   | 0x04 | 4    | IEEE 754                          |
| Float64   | 0x05 | 8    | IEEE 754                          |
| DateTime  | 0x06 | 8    | ticks (Int64)                     |
| Guid      | 0x07 | 16   | raw GUID                          |
| String    | 0x10 | var  | [int32][UTF-8]                    |
| Binary    | 0x11 | var  | [int32][raw bytes]                |
| Decimal   | 0x20 | var  | packed BCD or unsigned short form |
|           |      |      |                                   |
