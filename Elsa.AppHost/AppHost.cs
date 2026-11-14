using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Match the reference quickstart's PostgreSQL major and data-directory layout.
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("17-alpine")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);
var database = postgres.AddDatabase("elsa");

// Ignore the standalone launch profile's fixed ports; Aspire allocates both endpoints.
var server = builder.AddProject<Projects.Elsa_Server_Web>("server", options => options.ExcludeLaunchProfile = true)
    // With no launch profile, these select the framework for Aspire's `dotnet run` launcher.
    .WithArgs("--framework", "net11.0")
    .WithHttpEndpoint()
    .WithHttpsEndpoint()
    .WithReference(database, "PostgreSql")
    .WaitFor(database)
    .WithHttpHealthCheck("/health/ready")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Scripting__Python__AllowHostCodeExecution", "false");

var https = server.GetEndpoint("https");
server.WithEnvironment("Http__BaseUrl", https);

// Tenant configuration overrides the root settings when a workflow executes in a tenant.
// The reference server defines the default tenant and two sample tenants.
for (var tenantIndex = 0; tenantIndex < 3; tenantIndex++)
{
    var prefix = $"Multitenancy__Tenants__{tenantIndex}__Configuration__";
    server.WithEnvironment($"{prefix}ConnectionStrings__PostgreSql", database.Resource.ConnectionStringExpression)
        .WithEnvironment($"{prefix}Http__Host", https.Property(EndpointProperty.HostAndPort));
}

builder.Build().Run();
