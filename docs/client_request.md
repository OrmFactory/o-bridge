# o-Connector Protocol Specification (Client Request Format)

## Overview

The **o-Connector** protocol is a lightweight, binary, TCP-based protocol designed for fast and memory-efficient interaction with Oracle databases.

Each **TCP connection represents a single logical Oracle session**. The protocol follows a request-response model with streaming support for query results. All commands are processed sequentially. This document defines the structure and semantics of **client-to-server requests**. Server responses are covered in a separate document.

This document describes the structure of **client-to-server requests**. Server responses are covered in a separate document.

## Session Handshake

Each TCP connection begins with a **fixed-length handshake header**, which defines protocol capabilities, version, and compression support.

### Handshake Header Format

| Field      | Size (bytes) | Description                                  |
| ---------- | ------------ | -------------------------------------------- |
| `Magic`    | 4            | ASCII `"OCON"` (0x4F 0x43 0x4F 0x4E)         |
| `Version`  | 1            | Protocol version (currently `0x01`)          |
| `Flags`    | 1            | Bitmask: e.g., `0x01` = compression enabled  |
| `Reserved` | 2            | Must be zero. Reserved for future extensions |
Total: **8 bytes**. This header must be sent immediately upon opening the TCP connection.

```
4F 43 4F 4E   // 'OCON'
01           // Protocol version 1
01           // Flags: compression enabled (0x01)
00 00        // Reserved
```
## Request Frame Structure

All client requests follow a common binary format:

```
[CommandCode: byte]
[PayloadLength: int32]   // big-endian
[Payload: bytes]
```

- `CommandCode`: identifies the type of command (see command list below).
- `PayloadLength`: length of the payload in bytes (not including the header).
- `Payload`: command-specific data.

### Invalid Handshake

If the magic header is invalid or the version is unsupported, the server **must close the connection immediately**.
## Authentication

After sending the 8-byte handshake, the client **must** send **one authentication command**, depending on the chosen mode.

### Two supported modes:

| Mode            | Command                 | Description                                   |
| --------------- | ----------------------- | --------------------------------------------- |
| **Full string** | `ConnectFull` (`0x02`)  | Passes raw connection string to Oracle driver |
| **Named login** | `ConnectNamed` (`0x03`) | Sends username/password separately            |
Only one of these commands must be used per session, and it must come **immediately after the handshake**.

## ConnectFull (`0x02`)

### Payload:

```
[7BitEncodedInt: length of connection string]
[UTF-8 bytes: Oracle connection string]
```

## ConnectNamed (`0x03`)

### Payload:

```
[7BitEncodedInt: server name length]
[UTF-8 bytes: server name]
[7BitEncodedInt: login length]
[UTF-8 bytes: login]
[7BitEncodedInt: password length]
[UTF-8 bytes: password]
```
Server will use environment or fixed configuration to resolve remaining Oracle connection details.

## Execution Model

- Commands are executed **strictly sequentially**.
- Only one active command may be in progress at a time.
- Query commands (`0x20`) initiate a **streaming result**.
- While a query is in progress, only `CancelFetch` is permitted.
## Commands
| Code (`byte`) | Command               | Payload Details            | Description                                |
| ------------- | --------------------- | -------------------------- | ------------------------------------------ |
| `0x10`        | `BeginTransaction`    | _(empty)_                  | Starts an Oracle transaction               |
| `0x11`        | `CommitTransaction`   | _(empty)_                  | Commits the current transaction            |
| `0x12`        | `RollbackTransaction` | _(empty)_                  | Rolls back the current transaction         |
| `0x20`        | `Query`               | `SqlText + ParameterBlock` | Executes a SQL command (SELECT, DML, etc.) |
| `0x30`        | `CancelFetch`         | _(empty)_                  | Terminates an in-progress query stream     |
## Query Command (`0x20`)

The `Query` command is used for **all SQL execution**, including:

- `SELECT`
- `INSERT`, `UPDATE`, `DELETE`
- DDL statements (`CREATE TABLE`, etc.)

### Payload Structure:

```
[SqlLength: int32]
[SqlText: UTF-8 bytes]
[ParameterBlock: bytes] // optional, see parameter encoding spec
```

- `SqlText` must be a valid UTF-8 encoded Oracle SQL statement.
- `ParameterBlock` (if used) encodes parameters in a custom binary format (defined separately).

## CancelFetch Command (`0x30)

If a query is currently streaming rows to the client, the client may issue a `CancelFetch` request to instruct the server to stop sending further rows.

- This command is only valid while a `Query` is in progress.
- The server will stop sending row data, close the cursor, and send a final message (e.g., `QueryDone` with `canceled = true`).

## Notes on Transaction Handling

- Transactions are session-scoped.
- If the client closes the connection without committing, Oracle will **automatically roll back**.
- Transactions do not require client-supplied IDs - only `Begin`, `Commit`, and `Rollback` commands are needed.
## Behavior

- Each TCP socket maps to **one active Oracle session**.
- Closing the TCP connection cleanly terminates the session and rolls back any open transaction (per Oracle behavior).
- There is no need for explicit connection identifiers (`ConnId`) or session tokens..

## Query Result Behavior (Client-side)

The client is expected to infer:

| Property          | How it is determined                                  |
| ----------------- | ----------------------------------------------------- |
| `HasRows`         | `true` after receiving the first data row             |
| `RowCount`        | Counted by the client during row streaming            |
| `RecordsAffected` | Provided by the server in the query response metadata |

