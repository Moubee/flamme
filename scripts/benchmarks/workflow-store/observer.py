"""PostgreSQL diagnostics for the stock reference host. No fixture writes."""

import json
import subprocess
import time
from contextlib import contextmanager


@contextmanager
def session(psql):
    """One backend per psql process, even when using docker exec."""
    with subprocess.Popen(
        psql + ["-XqAtw", "-v", "ON_ERROR_STOP=1", "-P", "pager=off"],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True,
    ) as process:
        def query(sql):
            process.stdin.write(sql + ";\n\\echo END_OF_QUERY\n")
            process.stdin.flush()
            rows = []
            for line in process.stdout:
                if line.rstrip("\n") == "END_OF_QUERY":
                    return rows
                rows.append(line.rstrip("\n"))
            raise RuntimeError("psql exited before query completed; see its stderr")

        try:
            query("SET statement_timeout = '10s'; SET idle_in_transaction_session_timeout = '30s'")
            yield query
        finally:
            # EOF rolls back any remaining transaction.
            process.stdin.close()


def literal(value):
    return "'" + value.replace("'", "''") + "'"


def lock_instance(holder, instance_id):
    holder("BEGIN")
    rows = holder(f'''SELECT pg_backend_pid() FROM "Elsa"."WorkflowInstances"
                      WHERE "Id" = {literal(instance_id)} FOR UPDATE''')
    if len(rows) != 1:
        raise RuntimeError(f"Expected to lock one existing instance {instance_id}")
    return int(rows[0])


def await_writer(observer, holder_pid, hot_request):
    deadline = time.monotonic() + 8
    while time.monotonic() < deadline and not hot_request.done():
        rows = observer(f"""
            SELECT json_build_object('pid', pid, 'wait_event', wait_event, 'sql', query)
            FROM pg_stat_activity
            WHERE datname = current_database() AND state = 'active'
              AND wait_event_type = 'Lock' AND {holder_pid} = ANY(pg_blocking_pids(pid))
              AND query ~* '(UPDATE|INSERT INTO) [^;]*"WorkflowInstances"'
              AND query LIKE '%"Data"%'
        """)
        if len(rows) == 1:
            return json.loads(rows[0])
        time.sleep(0.01)
    details = hot_request.result() if hot_request.done() else "still pending"
    raise RuntimeError(f"No instance writer blocked by holder {holder_pid}; hot request {details}")


def committed(observer, definition, correlations, finished):
    """IDs for which both EF columns and the serialized callback state match."""
    status, substatus, bookmarks = ("Finished", "Finished", 0) if finished else ("Running", "Suspended", 1)
    values = ", ".join(f"({literal(i)}, {literal(c)})" for i, c in correlations.items())
    rows = observer(f"""
        WITH expected(id, correlation) AS (VALUES {values})
        SELECT i."Id" FROM "Elsa"."WorkflowInstances" i
        JOIN expected e ON i."Id" = e.id
        CROSS JOIN LATERAL (SELECT i."Data"::jsonb AS data) s
        WHERE i."CorrelationId" = e.correlation AND s.data->>'correlationId' = e.correlation
          AND i."DefinitionId" = {literal(definition)} AND s.data->>'definitionId' = i."DefinitionId"
          AND s.data->>'id' = i."Id" AND s.data->>'definitionVersionId' = i."DefinitionVersionId"
          AND i."Status" = '{status}' AND s.data->>'status' = i."Status"
          AND i."SubStatus" = '{substatus}' AND s.data->>'subStatus' = i."SubStatus"
          AND NOT i."IsExecuting" AND i."IncidentCount" = 0
          AND i."DataCompressionAlgorithm" = 'None' AND (i."FinishedAt" IS NOT NULL) = {finished}
          AND jsonb_array_length(s.data #> '{{bookmarks,$values}}') = {bookmarks}
          AND (SELECT count(*) FROM "Elsa"."Bookmarks" b WHERE b."WorkflowInstanceId" = i."Id") = {bookmarks}
    """)
    return set(rows)
