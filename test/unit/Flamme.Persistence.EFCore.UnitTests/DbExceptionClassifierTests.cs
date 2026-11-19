using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Elsa.Persistence.EFCore.UnitTests;

public sealed class DbExceptionClassifierTests
{
    [Test]
    [Arguments("23505", true)]
    [Arguments("23503", false)]
    [Arguments("40001", false)]
    public async Task IsDuplicateKey_ClassifiesPostgreSqlStates(string sqlState, bool expected)
    {
        var exception = new PostgresException("write failed", "ERROR", "ERROR", sqlState);
        await Assert.That(DbExceptionClassifier.IsDuplicateKey(exception)).IsEqualTo(expected);
    }

    [Test]
    public async Task IsDuplicateKey_FindsWrappedPostgreSqlViolation()
    {
        var exception = new DbUpdateException("write failed", new PostgresException("duplicate", "ERROR", "ERROR", "23505"));
        await Assert.That(DbExceptionClassifier.IsDuplicateKey(exception)).IsTrue();
    }

    [Test]
    public async Task IsDuplicateKey_RejectsUnrelatedException()
    {
        await Assert.That(DbExceptionClassifier.IsDuplicateKey(new InvalidOperationException("write failed"))).IsFalse();
    }
}
