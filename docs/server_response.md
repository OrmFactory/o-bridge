# o-Connector Protocol: Server Response Format

## Overview

This document describes the server-to-client response format in the o-Connector protocol. Each client request receives exactly one top-level response, which may include further streamed data (e.g. table rows). Responses are always delivered in the same order as the corresponding requests.

---

## General Response Frame Format

```
[ResponseCode: byte]
[PayloadLength: int32]
[Payload: bytes]
```


All responses conform to this envelope. The format of the payload depends on the response type.

---

## Response Codes

| Code (`byte`) | Meaning               | Description                                          |
|---------------|------------------------|------------------------------------------------------|
| `0x00`        | `Success`              | General success with no data                        |
| `0x01`        | `SuccessWithData`      | Table result with header, followed by streamed rows |
| `0x03`        | `TransactionAccepted`  | Acknowledges `BeginTransaction` with ServerTxId     |
| `0x10`        | `Error`                | Indicates failure with structured error info        |
| `0x20`        | `StreamRow`            | One row of table data                               |
| `0x21`        | `StreamEnd`            | End of table stream                                 |

---

## Response Guarantees and Obligations

The server **must** respond to each request block in the order received. It **must** emit exactly **one top-level response** per request. If the result is tabular, it will be followed by a series of `StreamRow` packets terminated by `StreamEnd`.

### Connect Commands (`ConnectFull`, `ConnectNamed`)

- If the provided `ConnId` is not known, the server **must**:
  - Accept the connection and return `SuccessWithData`, where the returned table contains exactly one column (`ConnId: GUID`).
  - Or return an `Error (0x10)` and ignore all subsequent commands in the same request.

- Only one connect command is allowed per request.

### Transaction Commands

- On `BeginTransaction`, server must return `TransactionAccepted (0x03)` with a 4-byte `ServerTxId`.
- On `CommitTransaction` or `RollbackTransaction`, server returns `Success (0x00)` on success or `Error (0x10)` on failure.

### Query Command

- Always returns:
  - `SuccessWithData (0x01)` followed by streamed `StreamRow`s and a closing `StreamEnd (0x21)`.
  - Even scalar values (e.g. rows affected) are returned as a table with one row and one column.

### Error Handling

- Any invalid command results in `Error (0x10)`.
- If an error occurs during processing of a request batch, remaining commands are ignored.
- Errors during `StreamRow` emission may cause immediate termination.

---

## Table Format in Responses

The `SuccessWithData (0x01)` response includes:

```
[FlagsLength: byte] ; # of bytes of flags per row  
[ColumnCount: int16]  
Repeat ColumnCount times:  
[ColumnName: len+UTF8]  
[TypeCode: byte]  
[Precision: byte]  
[Scale: byte]  
[Nullable: byte]
```


Then:
- One or more `StreamRow` messages:
  - Each: `[RowLength: int32][RowData: bytes]`
- Final `StreamEnd (0x21)` with zero-length payload.

## Execution Semantics

- Each command in a request block is executed independently.
- If one command fails, subsequent commands are still executed.
- It is the client’s responsibility to use transactions if atomicity or command-level dependency is required.

## Cancellation

The client may cancel a long-running request by closing the TCP connection.  
This is the recommended and supported mechanism for interrupting execution and freeing resources on the server side.

The server must handle connection termination gracefully and release any in-progress transactions or resources associated with the `ConnId`.

Out-of-band cancellation via command is not supported in the base protocol.

---

## Summary

- One request → one top-level response.
- Tabular results use a streaming model: `Header → Rows → End`
- Scalar results are returned as 1×1 tables.
- BeginTransaction returns a ServerTxId.
- Errors terminate processing of the current request block.
