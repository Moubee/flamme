using Npgsql;
using NpgsqlTypes;

namespace Flamme.Workflows.PerformanceTests.WorkflowStore;

internal sealed class WorkflowHttpObserver(string connectionString)
{
    public async Task<int> CountMatchingAsync(string definition, IReadOnlyList<WorkflowHttpInstance> instances, bool finished,
        (string Id, string Version)? heldRow = null)
    {
        const string sql = """
            SELECT count(DISTINCT i."Id")::int, EXISTS (
                SELECT FROM "Elsa"."WorkflowInstances" anchor
                WHERE anchor."Id" = @anchorId AND anchor.xmin::text = @anchorVersion
            )
            FROM unnest(@ids, @correlations) AS expected(id, correlation)
            JOIN "Elsa"."WorkflowInstances" i ON i."Id" = expected.id
            CROSS JOIN LATERAL (
                SELECT CASE WHEN i."DataCompressionAlgorithm" = 'None' THEN i."Data"::jsonb END AS data
                OFFSET 0
            ) state
            CROSS JOIN LATERAL (
                SELECT coalesce(state.data #> '{bookmarks,$values}', state.data->'bookmarks') AS bookmarks,
                       coalesce(state.data #> '{incidents,$values}', state.data->'incidents') AS incidents
            ) collections
            CROSS JOIN LATERAL (
                SELECT count(*) AS total, max(b."Id") AS id,
                       bool_and(b."Name" = 'Signals.Event' AND b."CorrelationId" = expected.correlation) AS matches
                FROM "Elsa"."Bookmarks" b WHERE b."WorkflowInstanceId" = i."Id"
            ) stored
            WHERE i."DefinitionId" = @definition AND i."CorrelationId" = expected.correlation
              AND state.data->>'id' = i."Id" AND state.data->>'correlationId' = i."CorrelationId"
              AND state.data->>'definitionId' = i."DefinitionId"
              AND state.data->>'definitionVersionId' = i."DefinitionVersionId"
              AND i."Status" = @status AND state.data->>'status' = i."Status"
              AND i."SubStatus" = @substatus AND state.data->>'subStatus' = i."SubStatus"
              AND NOT i."IsExecuting" AND state.data->'isExecuting' = 'false'::jsonb
              AND (i."FinishedAt" IS NOT NULL) = @finished
              AND ((state.data->>'finishedAt') IS NOT NULL) = @finished
              AND i."IncidentCount" = 0 AND collections.incidents = '[]'::jsonb
              AND CASE WHEN @finished THEN
                    stored.total = 0 AND collections.bookmarks = '[]'::jsonb
                  ELSE
                    stored.total = 1 AND stored.matches
                    AND jsonb_array_length(collections.bookmarks) = 1
                    AND collections.bookmarks #>> '{0,id}' = stored.id
                  END
            """;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        const NpgsqlDbType texts = NpgsqlDbType.Array | NpgsqlDbType.Text;
        command.Parameters.AddWithValue("ids", texts, instances.Select(instance => instance.Id).ToArray());
        command.Parameters.AddWithValue("correlations", texts, instances.Select(instance => instance.Correlation).ToArray());
        command.Parameters.AddWithValue("definition", definition);
        command.Parameters.AddWithValue("status", finished ? "Finished" : "Running");
        command.Parameters.AddWithValue("substatus", finished ? "Finished" : "Suspended");
        command.Parameters.AddWithValue("finished", finished);
        command.Parameters.AddWithValue("anchorId", heldRow?.Id ?? "");
        command.Parameters.AddWithValue("anchorVersion", heldRow?.Version ?? "");
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        if (heldRow.HasValue && !reader.GetBoolean(1))
            throw new InvalidOperationException($"Held instance {heldRow.Value.Id} no longer has its pre-write row version.");
        return reader.GetInt32(0);
    }

    public static async Task<(string Id, string Version)> LockAsync(NpgsqlConnection holder, NpgsqlTransaction transaction, string instanceId)
    {
        // The anchor's xmin identifies its exact pre-write row, including serialized state.
        // A resumed workflow may already have removed its separate bookmark before the instance update waits.
        const string sql = """
            SELECT xmin::text FROM "Elsa"."WorkflowInstances" WHERE "Id" = @id FOR UPDATE
            """;
        await using var command = new NpgsqlCommand(sql, holder, transaction);
        command.Parameters.AddWithValue("id", instanceId);
        return await command.ExecuteScalarAsync() is string version
            ? (instanceId, version)
            : throw new InvalidOperationException($"Workflow instance {instanceId} did not exist when acquiring its row lock.");
    }

    public static async Task WaitForWriterAsync(NpgsqlConnection observer, int holderPid, Task anchor)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT FROM pg_stat_activity
                WHERE datname = current_database() AND state = 'active' AND wait_event_type = 'Lock'
                  AND @holder = ANY(pg_blocking_pids(pid))
                  AND query ~* '(UPDATE|INSERT\s+INTO)\s+"Elsa"\."WorkflowInstances"[^;]*"Data"'
            )
            """;
        await using var command = new NpgsqlCommand(sql, observer);
        command.Parameters.AddWithValue("holder", holderPid);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            while (true)
            {
                if (anchor.IsCompleted)
                {
                    await anchor;
                    throw new InvalidOperationException("Anchor request completed before its workflow instance write was observed waiting.");
                }
                if (await command.ExecuteScalarAsync(deadline.Token) is true)
                    return;
                await Task.Delay(10, deadline.Token);
            }
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"No workflow instance writer was observed waiting on PostgreSQL backend {holderPid} within eight seconds.", ex);
        }
    }
}
