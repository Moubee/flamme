using System.Text;
using System.Text.Json;
using Flamme.Common.Models;
using Flamme.Extensions;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Persistence.EFCore.Modules.Management;
using Flamme.Persistence.EFCore.Modules.Runtime;
using Flamme.Workflows;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Management.Models;
using Flamme.Workflows.Models;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Authoring;
using Flamme.Workflows.Runtime.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var database = Environment.GetEnvironmentVariable("FLAMME_SAMPLE_DB") ?? throw new InvalidOperationException("Set FLAMME_SAMPLE_DB to a disposable PostgreSQL database.");
var definitionId = Environment.GetEnvironmentVariable("FLAMME_SAMPLE_DEFINITION") ?? "mail-notices";
var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
services.AddFlamme(module => module
    .UseWorkflowManagement(management => management.UseEntityFrameworkCore(ef => ef.UsePostgreSql(database)))
    .UseWorkflowRuntime(runtime => runtime.UseEntityFrameworkCore(ef => ef.UsePostgreSql(database))));
using var activityOutput = new StringWriter();
services.AddSingleton<IStandardOutStreamProvider>(new StandardOutStreamProvider(activityOutput));
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var host = scope.ServiceProvider;

if (args is ["init"])
{
    await using var management = await host.GetRequiredService<IDbContextFactory<ManagementFlammeDbContext>>().CreateDbContextAsync();
    await management.Database.MigrateAsync();
    await using var runtime = await host.GetRequiredService<IDbContextFactory<RuntimeFlammeDbContext>>().CreateDbContextAsync();
    await runtime.Database.MigrateAsync();
    return;
}

await host.GetRequiredService<IRegistriesPopulator>().PopulateAsync();
switch (args)
{
    case ["publish", var release]:
        var published = await Publish.NoticesAsync(host.GetRequiredService<CuratedWorkflowPublisher>(), definitionId, release);
        Print(new { revision = published.Id, version = published.Version });
        break;
    case ["start"]:
        var client = await host.GetRequiredService<IWorkflowRuntime>().CreateClientAsync();
        var result = await client.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(definitionId, VersionOptions.Published)
        });
        await ShowInstanceAsync(result.WorkflowInstanceId);
        break;
    case ["resume", var id]:
        await host.GetRequiredService<IEventPublisher>().PublishAsync("Approved", workflowInstanceId: id);
        await ShowInstanceAsync(id);
        break;
    case ["export", var version, var file]:
        var export = await host.GetRequiredService<IWorkflowDefinitionExporter>().ExportAsync(definitionId, VersionOptions.SpecificVersion(int.Parse(version)));
        await File.WriteAllBytesAsync(file, export?.Data ?? throw new InvalidOperationException("Definition revision was not found."));
        break;
    case ["import", var file]:
        var json = await File.ReadAllTextAsync(file, Encoding.UTF8);
        var model = host.GetRequiredService<IApiSerializer>().Deserialize<WorkflowDefinitionModel>(json);
        model.Id = null!;
        model.DefinitionId = definitionId;
        var imported = await host.GetRequiredService<IWorkflowDefinitionImporter>().ImportAsync(new SaveWorkflowDefinitionRequest { Model = model, Publish = true });
        if (!imported.Succeeded)
            throw new InvalidOperationException(string.Join(", ", imported.ValidationErrors.Select(error => error.Message)));
        Print(new { revision = imported.WorkflowDefinition.Id, version = imported.WorkflowDefinition.Version });
        break;
    default:
        throw new ArgumentException("Use init, publish RELEASE, start, resume INSTANCE_ID, export VERSION FILE, or import FILE.");
}

async Task ShowInstanceAsync(string id)
{
    var instance = await host.GetRequiredService<IWorkflowInstanceStore>().FindAsync(new WorkflowInstanceFilter { Id = id })
                   ?? throw new InvalidOperationException("Workflow instance was not found.");
    Print(new
    {
        instance = id, revision = instance.DefinitionVersionId, status = instance.SubStatus.ToString(),
        incidents = instance.IncidentCount, bookmarks = instance.WorkflowState.Bookmarks.Select(bookmark => new { bookmark.Name, bookmark.ActivityId }),
        lines = activityOutput.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
    });
}

static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonSerializerOptions.Web));
