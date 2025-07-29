# `o-Connector`: Type Serialization Format

This document describes **binary serialization rules** for all supported value types in the o-Connector protocol. It is intended for both client and server implementations that serialize/deserialize tabular data and parameters.
## Assumptions

- **Type and nullability** are always known (defined in headers).  
- Null values are not serialized — tracked via separate bitmaps.
- All values are written in **binary, unambiguous format**, optimized for streaming.
## Variable-size Types

### `String` (0x10)

```
[7BitEncodedInt Length][UTF-8 bytes]
```

- Length in bytes (not characters).
- Encoding is UTF-8 without BOM.
- No null-terminator.
### `Binary` (0x11)

```
[7BitEncodedInt Length][Raw Bytes]
```

- Used for Oracle `RAW`, `BLOB`, etc.
- Uncompressed, raw byte stream.
## NUMBER (0x20)

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

## Timestamp

| Field               | Bits | Range                  | Notes                                                       |
| ------------------- | ---- | ---------------------- | ----------------------------------------------------------- |
| `DateOnly`          | 1    | 0/1                    | If 1 → only date is stored, no fraction, no timezone offset |
| `HasFraction`       | 1    | 0/1                    | If 1 → fractional seconds included                          |
| `HasTimezoneOffset` | 1    | 0/1                    | If 1 → 2 extra bytes (UTC offset in minutes)                |
| `YearSign`          | 1    | 0=positive, 1=negative | Sign bit                                                    |
| `Year`              | 14   | 0 to 9999              | 14 bit integer                                              |
| `Month`             | 4    | 1–12                   |                                                             |
| `Day`               | 5    | 1–31                   |                                                             |
| `Hour`              | 5    | 0–23                   |                                                             |
| `Minute`            | 6    | 0–59                   |                                                             |
| `Second`            | 6    | 0–59                   |                                                             |
| `Fraction`          | 0–30 | 0–999_999_999          | depending on precision (up to 9 digits), if `HasFraction=1` |
| `TimezoneSign`      | 1    | 0=positive, 1=negative |                                                             |
| `Timezone offset`   | 10   | 0–840                  | Offset in minutes from UTC, absolute value                  |
### Fraction part

| Precision (digits) | Bit Count | Max Value   |
|--------------------|-----------|-------------|
| 1                  | 4         | 9           |
| 2                  | 7         | 99          |
| 3                  | 10        | 999         |
| 4                  | 14        | 9999        |
| 5                  | 17        | 99_999      |
| 6                  | 20        | 999_999     |
| 7                  | 24        | 9_999_999   |
| 8                  | 27        | 99_999_999  |
| 9                  | 30        | 999_999_999 |

## Summary Table

| Type Name           | Code | Size | Description                       |
| ------------------- | ---- | ---- | --------------------------------- |
| Boolean             | 0x01 | 1    | `0x00` or `0x01`                  |
| Float               | 0x04 | 4    | IEEE 754                          |
| Double              | 0x05 | 8    | IEEE 754                          |
| DateTime            | 0x06 | var  |                                   |
| IntervalDayToSecond | 0x07 | var  |                                   |
| IntervalYearToMonth | 0x08 | var  |                                   |
| Guid                | 0x09 | 16   | raw GUID                          |
| String              | 0x10 | var  | [int32][UTF-8]                    |
| Binary              | 0x11 | var  | [int32][raw bytes]                |
| Number              | 0x20 | var  | packed BCD or unsigned short form |
|                     |      |      |                                   |
