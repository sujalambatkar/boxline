# BoxLine

A mini NVOCC (container shipping line) operations system: bookings, container tracking,
vessels and voyages, detention/demurrage tariffs and documents.

> Work in progress. Phases 0 (foundation) and 1 (reference data and container master) are complete; see the roadmap below.

## Stack

| Layer | Technology |
|---|---|
| API | ASP.NET Core Web API on .NET 10, JWT auth, Swagger |
| Domain / data | C#, EF Core 10, Dapper (reports), SQL Server 2022 |
| Web | Angular 15 (NgModules), Angular Material 15, RxJS, SignalR client |
| Tests | xUnit, FluentAssertions, Jasmine/Karma |

## Layout

```
src/BoxLine.Api             controllers, JWT, Swagger, CORS, health
src/BoxLine.Core            entities and domain rules (no EF / ASP.NET references)
src/BoxLine.Infrastructure  EF Core context, migrations, repositories
tests/BoxLine.Tests         xUnit tests
web/                        Angular app
scripts/                    helper scripts
docs/                       diagrams and notes
```

## Prerequisites

.NET SDK 10, Node 18 (via nvm), Angular CLI 15, Docker Desktop, and a SQL Server 2022 container
named `boxline-sql` with a `BoxLine` database.

## Configuration

Secrets are never committed. Set them with user-secrets from `src/BoxLine.Api`:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=BoxLine;User Id=sa;Password=<sa-password>;TrustServerCertificate=True;"
dotnet user-secrets set "Jwt:Secret" "<random string, at least 32 characters>"
```

`src/BoxLine.Api/appsettings.Example.json` shows the expected shape.

In Development the API applies migrations and seeds 7 ports, 6 container types and 200 sample
containers (all with valid ISO 6346 check digits) on startup. It only seeds empty tables.

## Tests

Integration tests run against a separate `BoxLineTests` database on the same SQL Server (created and
seeded automatically), so they never modify development data. A guard test fails if that isolation breaks.

## Run

```bash
scripts/start-db.sh   # start the SQL Server container
scripts/run-api.sh    # http://localhost:5000  (Swagger: /swagger, health: /health)
scripts/run-web.sh    # http://localhost:4200
scripts/test.sh       # .NET + Angular tests
```

## Roadmap

0. Foundation (done)
1. Reference data and container master, ISO 6346 (done)
2. Auth and bookings
3. Container movements, tracking and live chat
4. Vessels and voyages
5. Tariffs and detention/demurrage calculator
6. Documents (Bill of Lading PDF, exports)
7. Performance demo
8. Polish and documentation
