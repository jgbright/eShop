# SigNoz Integration

This directory contains configuration files for the SigNoz observability platform integration with eShop.

## What is SigNoz?

SigNoz is an open-source observability platform that provides:
- **Distributed Tracing**: Track requests across microservices
- **Metrics Monitoring**: Monitor application and infrastructure metrics
- **Log Management**: Centralized log aggregation and analysis

## Architecture

The SigNoz integration includes a complete self-hosted stack:

1. **Zookeeper**: Distributed coordination for ClickHouse
2. **ClickHouse**: Time-series database for storing telemetry data
3. **Schema Migrator**: Initializes ClickHouse tables on first run
4. **OTel Collector**: Receives OpenTelemetry data from all services
5. **Query Service**: Backend API for querying telemetry data
6. **Frontend**: Web UI for visualizing and analyzing data

Container startup order is enforced:
```
Zookeeper → ClickHouse → Schema Migrator → (OTel Collector + Query Service) → Frontend
```

## Configuration Files

- `clickhouse-config.xml`: ClickHouse server configuration with Zookeeper integration
- `clickhouse-user-config.xml`: ClickHouse user profiles and quotas
- `otel-collector-config.yaml`: OpenTelemetry Collector pipeline configuration

## How to Use

### Default Mode (Aspire Dashboard)

By default, SigNoz containers start but **Aspire's built-in dashboard receives all telemetry**.
This allows you to use the familiar Aspire dashboard while having SigNoz available.

```bash
dotnet run --project src/eShop.AppHost
```

SigNoz UI is accessible at http://localhost:3301 but won't show data unless enabled.

### SigNoz Mode (Full Observability)

To redirect all telemetry to SigNoz instead of Aspire dashboard:

**Windows (PowerShell):**
```powershell
$env:ESHOP_USE_SIGNOZ=1
dotnet run --project src/eShop.AppHost
```

**Linux/macOS:**
```bash
ESHOP_USE_SIGNOZ=1 dotnet run --project src/eShop.AppHost
```

**VS Code launch.json:**
```json
{
  "env": {
    "ESHOP_USE_SIGNOZ": "1"
  }
}
```

When enabled, all eShop services send telemetry to SigNoz:
- HTTP request traces
- Database query traces
- Message queue operations
- Custom application metrics
- Structured logs

## Accessing SigNoz

Once enabled and the application is running:
- **URL**: http://localhost:3301
- The UI link appears in the Aspire Dashboard resource list
- First-time setup: Create a user account in the SigNoz UI

## Data Persistence

SigNoz uses persistent Docker volumes to retain data between restarts:
- `signoz-zookeeper-data`: Zookeeper coordination data
- `signoz-clickhouse-data`: All telemetry data (traces, metrics, logs)
- Configuration files are bind-mounted from this directory using absolute paths

## Customization

To customize the SigNoz configuration:
1. Modify the YAML/XML files in this directory
2. Restart the application for changes to take effect
3. For advanced scenarios, edit `Program.cs` to adjust container configuration

## Version Information

- Zookeeper: 3.9.1 (bitnami)
- ClickHouse: 24.1.2-alpine
- SigNoz Schema Migrator: 0.51.0
- SigNoz OTel Collector: 0.102.8
- SigNoz Query Service: 0.51.0
- SigNoz Frontend: 0.51.0

## Troubleshooting

**SigNoz UI shows no data:**
- Ensure `ESHOP_USE_SIGNOZ=1` is set before starting the AppHost
- Check that the schema migrator completed successfully in Aspire Dashboard
- Verify OTel Collector logs show incoming telemetry

**ClickHouse errors:**
- Wait for Zookeeper to be fully ready (check Aspire Dashboard)
- Ensure schema migrator completed (check container logs)
- Check that bind mount paths are correct (absolute paths)

**Aspire dashboard empty when using SigNoz:**
- This is expected when `ESHOP_USE_SIGNOZ=1` is set
- Telemetry is redirected to SigNoz instead of Aspire
- Remove the environment variable to restore Aspire dashboard telemetry
