# o-Connector Protocol Specification (Client Request Format)

## Overview

The o-Connector protocol is a binary protocol designed to bridge Oracle databases with lightweight clients using a minimal, fast, and memory-efficient approach. The protocol is built on top of plain TCP and uses a request-response model with streaming support.

This document describes the structure of **client-to-server requests**. Server responses are covered in a separate document.

## Connection Lifecycle

- Each TCP connection is considered a logical communication channel.
- A `ConnId` (GUID) can be assigned by the client or requested from the server.
- The `ConnId` is used to associate stateful context like transactions or pooled connections.
- If `ConnId` is not included, the server will require a `Hello` command or assign one implicitly.
- An empty request acts as a ping.

## Request Frame Structure

All client requests follow a common binary format:

```
[CommandCode: byte]
[PayloadLength: int32]
[Payload: bytes]
```

The `Payload` format depends on the command.

## Commands

| Code (`byte`) | Command               | Payload Details                                                            |
| ------------- | --------------------- | -------------------------------------------------------------------------- |
| `0x01`        | `Hello`               | `[ConnId: 16 bytes]` Optional initialization.                              |
| `0x02`        | `ConnectFull`         | `[ConnId: 16 bytes][ConnectionString: length-prefixed UTF8]`               |
| `0x03`        | `ConnectNamed`        | `[ConnId: 16 bytes][Login: len+UTF8][Password: len+UTF8]`                  |
| `0x10`        | `BeginTransaction`    | `[ConnId][ClientTxId: int32]`                                              |
| `0x11`        | `CommitTransaction`   | `[ConnId][TxIdMode: byte][TxId: int32]`                                    |
| `0x12`        | `RollbackTransaction` | `[ConnId][TxIdMode: byte][TxId: int32]`                                    |
| `0x20`        | `Query`               | `[ConnId][TxIdMode: byte][TxId: int32][SqlText: len+UTF8][ParameterBlock]` |
| `0x30`        | `Disconnect`          | `[ConnId]`                                                                 |

### Reserved

| Code   | Reserved For    |
| ------ | --------------- |
| `0x40` | `DescribeTable` |
| `0x50` | `ServerInfo`    |

## ConnId Handling

- The `ConnId` is always 16 bytes (GUID format).
- It identifies the logical connection context and associated Oracle session.
- Clients may reuse the same `ConnId` across multiple TCP connections.
- Invalid or unknown `ConnId`s may be rejected or delayed by the server.

## Transaction ID Handling

For commands that participate in transactions:

```
[TxIdMode: byte] // 0 = none, 1 = ClientTxId, 2 = ServerTxId
[TxId: int32]    // if TxIdMode != 0
```

- `ClientTxId` is specified in `BeginTransaction` and reused in chained requests.
- `ServerTxId` is returned by the server in response and used in standard transactional flows.

## Behavior

- Clients may initiate transactions and execute multiple commands in a single request cycle.
- The `ConnId` must be consistent across related requests.
- Empty requests (no command code) act as ping and may receive an empty or delayed response.

## Out of Scope (for request format)

- Response and error format
- Table metadata and data serialization
- Compression and transport-level encryption

## TODO (covered in separate documents)

- Server response frame and error handling
- Parameter encoding format
- Query result (table) format
- Type system definition
- Server-side connection and transaction mapping

