# O-Bridge

O-Bridge is an open-source proxy and binary protocol enabling [OrmFactory](https://ormfactory.com/) to interact with Oracle databases without violating Oracle licensing - no official connector is bundled. It facilitates ORM use by decoupling the proprietary Oracle driver.
## Features

- Eliminates Oracle round-trip latency via streaming FETCH.
- Supports `ExecuteReader` without explicitly opening a connection (saves round-trip).
- Very compact binary protocol - lower bandwidth than native Oracle.
- Optional compression (zstd) and optional encryption.
- Designed to run close to the database, e.g., on the same host (Oracle Linux).
## Architecture

Client ←→ O-Bridge ←→ Oracle

- Typically deployed on the same host as Oracle (e.g. Oracle Linux) or close to.
- Connects to Oracle using its native protocol and exposes a custom open protocol to clients.
- Simple startup: `git clone` + `dotnet run`.
- [ADO.NET connector](https://github.com/OrmFactory/o-connector-net) is published in a separate repo.
### Client Connections

- O-Bridge listens on both plain and SSL ports.
- Two modes:
    1. **Full proxy** (default): client supplies Oracle connection parameters, O-Bridge just forwards.
    2. **Defined users**: client logs in with own credentials, O-Bridge uses preset Oracle credentials — facilitates credential sharing without exposing the Oracle password.

- See the [ADO.NET connector repo](https://github.com/OrmFactory/o-connector-net) for connection string details.
### Protocol

Custom asynchronous binary protocol over TCP. Detailed specifications are in:

- [Client Request](docs/client_request.md)
- [Server Response](docs/server_response.md)
- [Data Types](docs/types.md)

See the corresponding docs in the repository for full details.
## Installation & Running

How to test:
```bash
git clone https://github.com/OrmFactory/o-bridge.git
cd o-bridge
dotnet run
```

- Distributed as source code only - **no binaries due to licensing**.
- Requires .NET Core SDK.
- Starts with default settings.

If you don't want to install .NET Core runtimes, just build with --self-contained and copy into destination machine.
### Building Self-Contained Binaries

For hosts without .NET:
```bash
dotnet publish -c Release -r linux-x64 --self-contained true -o ./publish
scp -r publish/ user@oracle-linux:/opt/obridge
```

Adjust runtime identifiers (e.g. `linux-arm64`, `win-x64`) as needed. Refer to the .NET runtime identifiers catalog.
### Deploy

**Systemd service (Oracle Linux)**:
```bash
# /etc/systemd/system/obridge.service
[Unit]
Description=O-Bridge Server
After=network.target

[Service]
Type=simple
User=obridge
WorkingDirectory=/opt/obridge/publish
ExecStart=/opt/obridge/publish/o-bridge
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

deploy example:
```bash
git clone https://github.com/OrmFactory/o-bridge.git /opt/obridge
cd /opt/obridge

#build
dotnet publish -c Release -o /opt/obridge/publish

#add user
sudo useradd -r -s /bin/false obridge
```

Start service:
```bash
sudo systemctl daemon-reload
sudo systemctl enable obridge
sudo systemctl start obridge
```
## Configuration

On first run, O-Bridge creates `config/` and `certs/`:
- `config/` contains `config.sample.yaml`; copy to `config.yaml` to customize.
- `certs/` holds a self-signed generated `default.pfx` if no certificate is specified.

Sample `config.yaml`:

```yaml
# Enable full proxy mode (default: true)
EnableFullProxy: true

# Enable zstd compression for traffic (default: true)
EnableCompression: true

# Port for non-SSL connections (default: 3855 / 0x0F0F)
PlainListenerPort: 3855

# Port for SSL connections (default: 4012 / 0x0FAC)
SslListenerPort: 4012

# Path to SSL certificate file in PFX format (default: null)
CertificatePath: null

# List of Oracle servers the bridge can connect to
Servers:
  - ServerName: "srv1"
    OracleHost: "127.0.0.1"
    # Optional
    #OraclePort: 1521

    # Use either SID or ServiceName, or both (default: null)
    OracleSID: "XE"
    OracleServiceName: null

    # Default credentials for connecting to the Oracle server
    OracleUser: "admin"
    OraclePassword: "password"

    # List of users allowed to connect through this server
    Users:
      - Name: "client_user"
        Password: "client_pass"
```

## Roadmap

- Add support for prepared statements
- Transactions
- `NextResult` (multiple result sets)  

[Contributions](CONTRIBUTING.md) welcome.

This project is not affiliated with, endorsed by, or sponsored by Oracle Corporation.
"Oracle" is a registered trademark of Oracle Corporation and/or its affiliates.

This library provides an alternative communication layer for Oracle-compatible clients.
