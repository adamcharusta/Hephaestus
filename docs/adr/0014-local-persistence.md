# ADR-0014: Local persistence in SQLite

- **Status:** Accepted
- **Date:** 2026-11-25
- **Spine:** AD-16, Consistency Conventions

## Context

Hephaestus needs durable Task state, an effect journal, installation settings, and a history kept indefinitely. A default installation must run on one machine with no additional server. Only free and open-source components are allowed. The Owner asked for a small, simple SQL database and may later want PostgreSQL or another engine.

## Decision

SQLite, in one file on a Docker volume, accessed through Entity Framework Core with one `DbContext` and one migration set.

- Write-ahead-log mode with a busy timeout.
- Timestamps are stored as fixed-width UTC ISO 8601 text, so that ordering works in queries.
- Raw agent output is not stored in the database. It is kept as files on the same volume, indexed from SQLite, and removed by a retention job (30 days by default). Structured history stays in SQLite indefinitely.
- On startup: back up the database with SQLite's backup operation, run migrations, and stop on failure leaving the backup. There is no automatic downgrade.
- Secrets are not in the database (ADR-0013).

## Alternatives considered

- **MySQL or PostgreSQL.** A separate server process means another container to run, back up, and upgrade. Rejected as the default. EF Core keeps other engines possible later.
- **Agent output in the database.** Large, rarely read, and it would dominate backups. Rejected.

## Consequences

- With one writer and one Task at a time, SQLite's single-writer model costs nothing.
- The data volume and the secrets volume together are the entire state of an installation.
- Running against an existing database server is out of scope for v1.
