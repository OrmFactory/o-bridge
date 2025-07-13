# o-Connector Protocol: Table Header Format

This document describes the **header format** used in o-Connector responses to represent column metadata for tabular results (including scalar queries, which are treated as 1x1 tables).
## Overview

Each resultset begins with a header block describing the structure of the table. This includes:

- The number of columns
- A bitfield indicating `NULL` values per row
- For each column:
  - Data type and nullability
  - Required and optional metadata
## Table Header Layout

```
[ColumnCount: int32]  
[NullBitmaskLength: int32] // number of bits required per row  
[ColumnHeader1]  
[ColumnHeader2]
```

- `ColumnCount`: number of columns returned in this resultset.
- `NullBitmaskLength`: number of **bits** required to store null flags for each row (rounded up to nearest byte).

## ColumnHeader Format

Each column header has the following layout:

```
[TypeCode: NUMBER]  
[IsNullable: 0]  
[FieldMask: 0b00000111] // bits 0,1,2  
[ColumnName: "price"]  
[DataTypeName: "NUMBER"]  
[Precision: 10]  
[Scale: 2]
```
### Notes:

- `TypeCode` corresponds to the binary format used in row serialization (see type serialization doc).
- `FieldMask` indicates presence of additional optional metadata.
- Fields appear in the same order as the mask bits.
- `ColumnName` is mandatory. It is never empty or null.

---

## FieldMask Bit Definitions

| Bit | Field Name      | Type       | Description                       |
| --- | --------------- | ---------- | --------------------------------- |
| 0   | DataTypeName    | len+UTF8   | Oracle name of data type          |
| 1   | Precision       | byte       | Numeric precision (if applicable) |
| 2   | Scale           | byte       | Numeric scale (if applicable)     |
| 3   | MaxLength       | int32      | For CHAR/VARCHAR/etc.             |
| 4   | IsIdentity      | byte (0/1) | Auto-increment flag               |
| 5   | IsPrimaryKey    | byte (0/1) | True if part of PK                |
| 6   | DefaultValueSql | len+UTF8   | Raw SQL expression for default    |
- Fields must appear in order, and only if the corresponding bit in `FieldMask` is set.

---

## Example

### Column: `price NUMBER(10,2) DEFAULT 0 NOT NULL`

Would serialize as:

```
[TypeCode: NUMBER]  
[IsNullable: 0]  
[FieldMask: 0b00000111] // bits 0,1,2  
[ColumnName: "price"]  
[DataTypeName: "NUMBER"]  
[Precision: 10]  
[Scale: 2]
```
## Guidelines

- **Minimal fields**: Only include fields that Oracle provides in `GetColumnSchemaAsync()`.
- **ColumnName is mandatory.**
- Fields not present in FieldMask **must not be serialized**.
- FieldMask allows forward compatibility and optional extensions.
