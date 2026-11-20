using System.Security.Cryptography;
using Testcontainers.PostgreSql;

namespace Flamme.Workflows.PerformanceTests.WorkflowStore;

internal static class WorkflowPostgres
{
    public static PostgreSqlContainer Create() => new PostgreSqlBuilder("postgres:16")
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
        .WithCreateParameterModifier(parameters => parameters.HostConfig!.PortBindings["5432/tcp"][0].HostIP = "127.0.0.1")
        .Build();
}
