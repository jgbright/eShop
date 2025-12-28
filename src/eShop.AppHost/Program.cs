using eShop.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

var redis = builder.AddRedis("redis");
var rabbitMq = builder.AddRabbitMQ("eventbus")
    .WithLifetime(ContainerLifetime.Persistent);
var postgres = builder.AddPostgres("postgres")
    .WithImage("ankane/pgvector")
    .WithImageTag("latest")
    .WithLifetime(ContainerLifetime.Persistent);

var catalogDb = postgres.AddDatabase("catalogdb");
var identityDb = postgres.AddDatabase("identitydb");
var orderDb = postgres.AddDatabase("orderingdb");
var webhooksDb = postgres.AddDatabase("webhooksdb");

// SigNoz components for self-hosted observability
// Complete topology includes: Zookeeper -> ClickHouse -> Schema Init -> Query Service & OTel Collector -> Frontend
var signozConfigPath = Path.Combine(builder.AppHostDirectory, "signoz");

// Zookeeper is required for ClickHouse distributed coordination
var zookeeper = builder.AddContainer("signoz-zookeeper", "bitnami/zookeeper")
    .WithImageTag("3.9.1")
    .WithEnvironment("ALLOW_ANONYMOUS_LOGIN", "yes")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("signoz-zookeeper-data", "/bitnami/zookeeper");

var clickhouse = builder.AddContainer("signoz-clickhouse", "clickhouse/clickhouse-server")
    .WithImageTag("24.1.2-alpine")
    .WithHttpEndpoint(port: 8123, targetPort: 8123, name: "http")
    .WithEndpoint(port: 9000, targetPort: 9000, name: "native")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithBindMount(Path.Combine(signozConfigPath, "clickhouse-config.xml"), "/etc/clickhouse-server/config.d/config.xml")
    .WithBindMount(Path.Combine(signozConfigPath, "clickhouse-user-config.xml"), "/etc/clickhouse-server/users.d/users.xml")
    .WithDataVolume("signoz-clickhouse-data")
    .WaitFor(zookeeper);

// Schema migrator creates required ClickHouse tables for SigNoz
var signozMigrator = builder.AddContainer("signoz-schema-migrator", "signoz/schema-migrator")
    .WithImageTag("0.51.0")
    .WithLifetime(ContainerLifetime.Session) // Runs once per session
    .WithEnvironment("STORAGE", "clickhouse")
    .WithEnvironment("CLICKHOUSE_HOST", "signoz-clickhouse")
    .WithEnvironment("CLICKHOUSE_PORT", "9000")
    .WithArgs("--dsn", "tcp://signoz-clickhouse:9000")
    .WaitFor(clickhouse);

var signozOtelCollector = builder.AddContainer("signoz-otel-collector", "signoz/signoz-otel-collector")
    .WithImageTag("0.102.8")
    .WithHttpEndpoint(port: 4317, targetPort: 4317, name: "grpc")
    .WithHttpEndpoint(port: 4318, targetPort: 4318, name: "http")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithBindMount(Path.Combine(signozConfigPath, "otel-collector-config.yaml"), "/etc/otel-collector-config.yaml")
    .WithArgs("--config", "/etc/otel-collector-config.yaml")
    .WaitFor(signozMigrator); // Wait for schema to be ready

var signozQueryService = builder.AddContainer("signoz-query-service", "signoz/query-service")
    .WithImageTag("0.51.0")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithEnvironment("STORAGE", "clickhouse")
    .WithEnvironment("CLICKHOUSE_HOST", "signoz-clickhouse")
    .WithEnvironment("CLICKHOUSE_PORT", "9000")
    .WithEnvironment("TELEMETRY_ENABLED", "true")
    .WithEnvironment("DEPLOYMENT_TYPE", "docker-standalone-amd")
    .WaitFor(signozMigrator); // Wait for schema to be ready

var signozFrontend = builder.AddContainer("signoz-frontend", "signoz/frontend")
    .WithImageTag("0.51.0")
    .WithHttpEndpoint(port: 3301, targetPort: 3301, name: "http")
    .WithExternalHttpEndpoints()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithEnvironment("FRONTEND_API_ENDPOINT", "http://signoz-query-service:8080")
    .WaitFor(signozQueryService);

// Configure all services to send OpenTelemetry data to SigNoz (opt-in via ESHOP_USE_SIGNOZ=1)
// By default, Aspire dashboard receives telemetry. Set ESHOP_USE_SIGNOZ=1 to use SigNoz instead.
builder.AddSigNozOpenTelemetry(signozOtelCollector);

var launchProfileName = ShouldUseHttpForEndpoints() ? "http" : "https";

// Services
var identityApi = builder.AddProject<Projects.Identity_API>("identity-api", launchProfileName)
    .WithExternalHttpEndpoints()
    .WithReference(identityDb);

var identityEndpoint = identityApi.GetEndpoint(launchProfileName);

var basketApi = builder.AddProject<Projects.Basket_API>("basket-api")
    .WithReference(redis)
    .WithReference(rabbitMq).WaitFor(rabbitMq)
    .WithEnvironment("Identity__Url", identityEndpoint);
redis.WithParentRelationship(basketApi);

var catalogApi = builder.AddProject<Projects.Catalog_API>("catalog-api")
    .WithReference(rabbitMq).WaitFor(rabbitMq)
    .WithReference(catalogDb);

var orderingApi = builder.AddProject<Projects.Ordering_API>("ordering-api")
    .WithReference(rabbitMq).WaitFor(rabbitMq)
    .WithReference(orderDb).WaitFor(orderDb)
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Identity__Url", identityEndpoint);

builder.AddProject<Projects.OrderProcessor>("order-processor")
    .WithReference(rabbitMq).WaitFor(rabbitMq)
    .WithReference(orderDb)
    .WaitFor(orderingApi); // wait for the orderingApi to be ready because that contains the EF migrations

builder.AddProject<Projects.PaymentProcessor>("payment-processor")
    .WithReference(rabbitMq).WaitFor(rabbitMq);

var webHooksApi = builder.AddProject<Projects.Webhooks_API>("webhooks-api")
    .WithReference(rabbitMq).WaitFor(rabbitMq)
    .WithReference(webhooksDb)
    .WithEnvironment("Identity__Url", identityEndpoint);

// Reverse proxies
builder.AddYarp("mobile-bff")
    .WithExternalHttpEndpoints()
    .ConfigureMobileBffRoutes(catalogApi, orderingApi, identityApi);

// Apps
var webhooksClient = builder.AddProject<Projects.WebhookClient>("webhooksclient", launchProfileName)
    .WithReference(webHooksApi)
    .WithEnvironment("IdentityUrl", identityEndpoint);

var webApp = builder.AddProject<Projects.WebApp>("webapp", launchProfileName)
    .WithExternalHttpEndpoints()
    .WithUrls(c => c.Urls.ForEach(u => u.DisplayText = $"Online Store ({u.Endpoint?.EndpointName})"))
    .WithReference(basketApi)
    .WithReference(catalogApi)
    .WithReference(orderingApi)
    .WithReference(rabbitMq).WaitFor(rabbitMq)
    .WithEnvironment("IdentityUrl", identityEndpoint);

// set to true if you want to use OpenAI
bool useOpenAI = false;
if (useOpenAI)
{
    builder.AddOpenAI(catalogApi, webApp, OpenAITarget.OpenAI); // set to AzureOpenAI if you want to use Azure OpenAI
}

bool useOllama = false;
if (useOllama)
{
    builder.AddOllama(catalogApi, webApp);
}

// Wire up the callback urls (self referencing)
webApp.WithEnvironment("CallBackUrl", webApp.GetEndpoint(launchProfileName));
webhooksClient.WithEnvironment("CallBackUrl", webhooksClient.GetEndpoint(launchProfileName));

// Identity has a reference to all of the apps for callback urls, this is a cyclic reference
identityApi.WithEnvironment("BasketApiClient", basketApi.GetEndpoint("http"))
           .WithEnvironment("OrderingApiClient", orderingApi.GetEndpoint("http"))
           .WithEnvironment("WebhooksApiClient", webHooksApi.GetEndpoint("http"))
           .WithEnvironment("WebhooksWebClient", webhooksClient.GetEndpoint(launchProfileName))
           .WithEnvironment("WebAppClient", webApp.GetEndpoint(launchProfileName));

builder.Build().Run();

// For test use only.
// Looks for an environment variable that forces the use of HTTP for all the endpoints. We
// are doing this for ease of running the Playwright tests in CI.
static bool ShouldUseHttpForEndpoints()
{
    const string EnvVarName = "ESHOP_USE_HTTP_ENDPOINTS";
    var envValue = Environment.GetEnvironmentVariable(EnvVarName);

    // Attempt to parse the environment variable value; return true if it's exactly "1".
    return int.TryParse(envValue, out int result) && result == 1;
}
