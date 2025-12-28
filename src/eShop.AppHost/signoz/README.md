# SigNoz Integration

This directory contains configuration files for the SigNoz observability platform integration with eShop.

## What is SigNoz?

SigNoz is an open-source observability platform that provides:
- **Distributed Tracing**: Track requests across microservices
- **Metrics Monitoring**: Monitor application and infrastructure metrics
- **Log Management**: Centralized log aggregation and analysis

## Components

The SigNoz integration includes four main components:

1. **ClickHouse**: Time-series database for storing telemetry data
2. **OTel Collector**: Receives OpenTelemetry data from all services
3. **Query Service**: Backend API for querying telemetry data
4. **Frontend**: Web UI for visualizing and analyzing data

## Configuration Files

- `clickhouse-config.xml`: ClickHouse server configuration
- `clickhouse-user-config.xml`: ClickHouse user profiles and quotas
- `otel-collector-config.yaml`: OpenTelemetry Collector pipeline configuration

## Accessing SigNoz

Once the application is running, you can access the SigNoz UI at:
- **URL**: http://localhost:3301
- The UI will be automatically linked in the Aspire Dashboard

## Features

All eShop services automatically send telemetry to SigNoz including:
- HTTP request traces
- Database query traces
- Message queue operations
- Custom application metrics
- Structured logs

## Data Persistence

SigNoz uses persistent volumes to retain data between restarts:
- ClickHouse data is stored in a Docker volume
- Configuration files are bind-mounted from this directory

## Customization

To customize the SigNoz configuration:
1. Modify the YAML/XML files in this directory
2. Restart the application for changes to take effect

## Version Information

- SigNoz OTel Collector: 0.102.8
- SigNoz Query Service: 0.51.0
- SigNoz Frontend: 0.51.0
- ClickHouse: 24.1.2-alpine
