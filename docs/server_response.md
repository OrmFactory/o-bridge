# o-Connector Protocol: Server Response Format

## Overview

This document defines the server-to-client response format in the o-Connector binary protocol. The protocol operates over a single TCP connection per session, with optional TLS and stream-wide compression (zstd). All commands produce a response, and query results are streamed as framed tables.

## Transport Layer & Compression

If the client enables compression during the handshake (`Compressed` flag), the **entire TCP session** (both directions) is wrapped in a continuous **zstd** stream.

- All message frames (headers and payloads) are compressed together.
- All `PayloadLength` values refer to the size **after decompression**.
- Compression is negotiated once per connection and cannot change mid-session.
- Compression occurs either inside or outside TLS depending on setup:
    - `TLS → zstd → protocol` (if TLS is enabled)
    - `zstd → protocol` (plain TCP + compression)
## General Response Frame Format
Each server response follows this structure:

```
[ResponseCode: byte]
[PayloadLength: int32]   // big-endian
[Payload: bytes]         // uncompressed length = PayloadLength
```

All sizes refer to the **uncompressed** payload. Responses are delivered in the same order as the corresponding requests.

---

## Response Codes

| Code (`byte`) | Name                  | Description                                                   |
| ------------- | --------------------- | ------------------------------------------------------------- |
| `0x00`        | `Success`             | Acknowledges a successful command with no additional data     |
| `0x01`        | `SuccessWithData`     | Begins a stream of tabular data (`StreamRow` and `StreamEnd`) |
| `0x03`        | `TransactionAccepted` | (Optional) Acknowledges `BeginTransaction` with ServerTxId    |
| `0x10`        | `Error`               | Command failed; includes error code and message               |
| `0x20`        | `StreamRow`           | Single row of tabular data in a query stream                  |
| `0x21`        | `StreamEnd`           | Terminates a streamed response                                |
## Command Response Semantics

### Connect Commands

- The server must respond with:
    - `Success (0x00)` if connection succeeded.
    - `Error (0x10)` if authentication failed or protocol violation occurred.

### Query Commands

- Always return:
    
    - `SuccessWithData (0x01)` → then
    - 0 or more `StreamRow (0x20)` → then
    - `StreamEnd (0x21)`

Even scalar or non-query operations (like `UPDATE`) return tabular results:

- For `UPDATE`, one row with one column `RecordsAffected`
- For `SELECT COUNT(*)`, one row with one column `RowsCount`

The client determines:

- `HasRows` = received at least one `StreamRow`
- `RowCount` = count of received rows

### Transactions

- `BeginTransaction` may return `TransactionAccepted (0x03)` with server-generated ID
- `CommitTransaction` and `RollbackTransaction` return `Success` or `Error`

### Errors

Any failure results in:

```
[ResponseCode: 0x10]
[PayloadLength]
[Payload: bytes]
```

The payload contains structured error information (e.g., error code, message, optional stack trace).  
If an error occurs during a query stream, the server may terminate the stream early and emit no further `StreamRow`/`StreamEnd`.

## Streaming Format

If `SuccessWithData (0x01)` is returned:

### Stream Header (included in `Payload` of 0x01):

```
[FlagsLength: byte]        // optional per-row flags (e.g. null mask)
[ColumnCount: int16]
Repeat ColumnCount times:
    [ColumnName: len+UTF8]
    [TypeCode: byte]
    [Precision: byte]
    [Scale: byte]
    [Nullable: byte]
```

Then the server emits:

- Zero or more:

```
[0x20 StreamRow]
[PayloadLength: int32]
[Payload: Row data (Flags + Column values)]
```

- Finally:

```
[0x21 StreamEnd]
[PayloadLength: int32]
[Payload: optional message or JSON]
```

## Cancellation Support

If the client sends `CancelFetch (0x30)`, the server:

- Stops sending `StreamRow` messages
- Immediately sends a `StreamEnd (0x21)` with cancellation info
- Frees any internal Oracle cursor or resources

The client must be ready to handle `StreamEnd` with partial data or a cancellation notice.

## Execution Model

- Each request receives exactly one top-level response
- Query results are streamed
- Commands are independent — no batching or grouping
- Transactions must be used explicitly if isolation is required
- The server does not multiplex multiple queries over a single session

## Summary

- One TCP connection = one session
- One request = one response
- Query results are streamed row-by-row
- Scalar results are represented as 1×1 tables
- Compression is stream-wide (zstd), enabled at handshake
- Cancellation is supported via `CancelFetch (0x30)`
- No `ConnId`, no logical session multiplexing