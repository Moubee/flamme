using Flamme.AI.Abstractions.Models;
using Flamme.AI.Persistence.EFCore.Stores;
using Flamme.Testing.Shared.Databases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Tasks;
using TUnit.Core.Interfaces;

namespace Flamme.AI.Persistence.EFCore.UnitTests;

public class EFCoreAIAuditSinkTests : IAsyncInitializer, IAsyncDisposable
{
    private static readonly NullLogger<EFCoreAIAuditSink> AuditLogger = NullLogger<EFCoreAIAuditSink>.Instance;
    private readonly PostgreSqlTestDatabase _connection = new();
    private AIDbContext _dbContext = default!;

    [Test]
    [DisplayName("Audit sink persists audit records")]
    public async Task AuditSinkPersistsAuditRecords()
    {
        var sink = new EFCoreAIAuditSink(_dbContext, AuditLogger);

        await sink.RecordAsync(new AIAuditEvent
        {
            Id = "audit-1",
            ActorId = "user-1",
            ConversationId = "conversation-1",
            Type = "prompt.submitted",
            Timestamp = DateTimeOffset.UtcNow,
            Summary = "Prompt submitted"
        });

        var record = await _dbContext.AuditRecords.SingleAsync();
        await Assert.That(record.Type).IsEqualTo("prompt.submitted");
        await Assert.That(record.Summary).IsEqualTo("Prompt submitted");
    }

    [Test]
    [DisplayName("Audit sink persists events with generated IDs")]
    public async Task AuditSinkPersistsEventsWithGeneratedIds()
    {
        var sink = new EFCoreAIAuditSink(_dbContext, AuditLogger);

        await sink.RecordAsync(new AIAuditEvent
        {
            ActorId = "user-1",
            Type = "prompt.submitted",
            Timestamp = DateTimeOffset.UtcNow
        });

        var record = await _dbContext.AuditRecords.SingleAsync();
        await Assert.That(string.IsNullOrWhiteSpace(record.Id)).IsFalse();
    }

    [Test]
    [DisplayName("Audit sink batches audit records")]
    public async Task AuditSinkBatchesAuditRecords()
    {
        var sink = new EFCoreAIAuditSink(_dbContext, AuditLogger);

        await sink.RecordManyAsync([
            new AIAuditEvent
            {
                Id = "audit-1",
                ActorId = "user-1",
                Type = "tool.invoked",
                Timestamp = DateTimeOffset.UtcNow
            },
            new AIAuditEvent
            {
                Id = "audit-2",
                ActorId = "user-1",
                Type = "tool.completed",
                Timestamp = DateTimeOffset.UtcNow
            }
        ]);

        var types = await _dbContext.AuditRecords.OrderBy(x => x.Id).Select(x => x.Type).ToListAsync();
        await Assert.That(types).IsEquivalentTo(["tool.invoked", "tool.completed"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [DisplayName("Audit sink rolls back batch records when a batch record fails")]
    public async Task AuditSinkRollsBackBatchRecordsWhenBatchRecordFails()
    {
        var sink = new EFCoreAIAuditSink(_dbContext, AuditLogger);

        await sink.RecordManyAsync([
            new AIAuditEvent
            {
                Id = "audit-duplicate",
                ActorId = "user-1",
                Type = "tool.invoked",
                Timestamp = DateTimeOffset.UtcNow
            },
            new AIAuditEvent
            {
                Id = "audit-duplicate",
                ActorId = "user-1",
                Type = "tool.completed",
                Timestamp = DateTimeOffset.UtcNow
            }
        ]);

        await Assert.That(await _dbContext.AuditRecords.AnyAsync()).IsFalse();
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-run database test exercises EF schema creation or migrations; production AOT support is tracked separately.")]
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _dbContext = new AIDbContext(new DbContextOptionsBuilder<AIDbContext>().UsePostgreSqlAIMigrations(_connection).Options);
        await _dbContext.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
