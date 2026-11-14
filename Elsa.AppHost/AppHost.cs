var builder = DistributedApplication.CreateBuilder(args);

// Match the reference quickstart's PostgreSQL major and data-directory layout.
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("17-alpine")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);
postgres.AddDatabase("elsa");

builder.Build().Run();
