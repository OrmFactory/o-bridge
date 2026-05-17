# O-Bridge

## Why?

When developing [OrmFactory](https://ormfactory.com), we faced a major roadblock: **Oracle's proprietary license strictly prohibits embedding their official drivers into commercial or third-party developer tools.** 

We refused to compromise on user experience or expose our enterprise clients to compliance risks. Since we couldn't ship Oracle's closed-source binaries, we decided to build our own independent, clean-room alternative:
1. An open-source, high-performance gateway [O-Bridge Server](https://github.com/OrmFactory/o-bridge) that sits next to the database under the permissive MIT license.
2. A lightweight managed client (**O-Connector**) that talks to the gateway via a highly optimized binary stream.

**The result?** A 100% legally clean, lightning-fast, and container-friendly communication layer. We use it to power OrmFactory, but we made the entire connectivity stack free and open-source forever under the MIT license, so you can safely use, embed, or modify it in your own projects without asking Oracle for permission.

It is designed specifically for use with [OrmFactory](https://ormfactory.com) - a modern developer tool for model-first approach and ORM code generation.
O-Connector enables OrmFactory to connect to Oracle in a fast and resource-efficient way, without relying on heavyweight native drivers.

## Features

- Eliminates Oracle round-trip latency via streaming FETCH.
- Supports `ExecuteReader` without explicitly opening a connection (saves round-trip).
- Very compact binary protocol - lower bandwidth than native Oracle.
- Optional compression (zstd) and optional encryption.
- Designed to run close to the database, e.g., on the same host (Oracle Linux).

## Architecture

O-Bridge fetches the official open-source Oracle Managed Driver via NuGet during your local compilation. This ensures 100% legal compliance because you build it yourself.

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
cd o-bridge/OBridge.Server
dotnet run
```

- Distributed as source code only - **no binaries due to licensing**.
- Requires .NET Core SDK.
- Starts with default settings.

If you don't want to install .NET Core runtimes, just build with --self-contained and copy into destination machine.

### Building Self-Contained Binaries

If you do not want to install the .NET SDK on your production database host, compile a standalone, self-contained single binary on your build machine:

```bash
# 1. Navigate to the project directory
cd o-bridge/OBridge.Server

# 2. Compile for target architecture (outputs to OBridge.Server/publish)
dotnet publish OBridge.Server.csproj -c Release -r linux-x64 --self-contained true -o ./publish

# 3. Transfer the compiled assets to your production host
scp -r ./publish user@oracle-linux-host:/opt/obridge-server
```
*(Adjust the Runtime Identifier like `linux-arm64` or `win-x64` based on your production host architecture).*

### Deploy & Service Setup (Oracle Linux 8/9, RHEL 8/9, Rocky Linux)

Execute these commands on your target production server to create a dedicated system user and set correct permissions:

```bash
sudo useradd -r -s /bin/false obridge
sudo mkdir -p /opt/obridge-server
sudo chown -R obridge:obridge /opt/obridge-server
```

#### Creating Systemd Service

Create the unit configuration file at `/etc/systemd/system/obridge.service`:

```ini
[Unit]
Description=O-Bridge Server Gateway
After=network.target

[Service]
Type=simple
User=obridge
WorkingDirectory=/opt/obridge-server
ExecStart=/opt/obridge-server/OBridge.Server
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

Start and enable the service:

```bash
sudo systemctl daemon-reload
sudo systemctl enable obridge
sudo systemctl start obridge
```

## Running via Docker (Build from Source)

If you prefer containerized deployments over systemd, you can easily build and run O-Bridge inside Docker. To maintain 100% licensing compliance, you must build the image locally from source so that dependencies are safely resolved within your own infrastructure environment.

### 1. Create a `Dockerfile`

Create a file named `Dockerfile` in the root of the repository with the following multi-stage specification:

```dockerfile
# Stage 1: Build the application and fetch official Oracle dependencies from NuGet
FROM ://microsoft.com AS build
WORKDIR /src

# Copy source code
COPY . .
WORKDIR /src/OBridge.Server

# Compile a self-contained production-ready binary
RUN dotnet publish OBridge.Server.csproj -c Release -r linux-x64 --self-contained true -o /app/publish

# Stage 2: Create a minimal, secure runtime image
FROM ://microsoft.com AS final
WORKDIR /app

# Ensure console logs are flushed instantly and preserve colors
ENV DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION=1
ENV DOTNET_PRINT_TELEMETRY_MESSAGE=false

# Run as a non-root system user for security
USER \$APP_UID

# Copy compiled assets and expose default O-Bridge ports
COPY --from=build /app/publish .
EXPOSE 3855 4012

ENTRYPOINT ["./OBridge.Server"]
```

### 2. Build and Run the Container

Execute these commands in the directory containing the `Dockerfile`:

```bash
# Build the image locally
docker build -t o-bridge-server .

# Run the container with persistent volumes for config and certs
docker run -d \
  --name o-bridge \
  -p 3855:3855 \
  -p 4012:4012 \
  -v \$(pwd)/config:/app/config \
  -v \$(pwd)/certs:/app/certs \
  --restart unless-stopped \
  o-bridge-server
```

### 3. Viewing Logs

Since O-Bridge streams structured single-line text directly to the standard console output, you can inspect the server logs in real-time using native Docker tools:

```bash
docker logs -f o-bridge
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
