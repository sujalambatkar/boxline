# BoxLine

A mini NVOCC (container shipping line) operations system: bookings, container tracking,
vessels and voyages, detention/demurrage tariffs and documents.

> Work in progress. Phases 0 (foundation), 1 (reference data and container master) and 2 (auth and bookings) are complete; see the roadmap below.

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

## Accounts and roles

On the first API start in Development, two accounts are created and their generated passwords are
printed to the console **once**: `ops@boxline.test` (Ops) and `shipper@boxline.test` (Shipper). To choose
the passwords yourself, set `Seed:OpsPassword` and `Seed:ShipperPassword` in user-secrets before that
first run. New shippers can self-register (`POST /api/auth/register`); Ops accounts cannot.

| | Shipper | Ops |
|---|---|---|
| Ports, container types | read | read and manage |
| Container master | no access | read and manage |
| Bookings | create, edit drafts, confirm, cancel, **own only** | everything |

## Booking rules

- Status workflow: `Draft -> Confirmed -> Cancelled`. A draft is confirmed (not cancelled directly), and
  only a confirmed booking can be cancelled. Defined in one table in `BookingWorkflow`.
- Only drafts can be edited. Port of discharge must differ from port of loading; at least one container
  line; quantity 1 to 50 per line; one line per container type.
- Booking numbers look like `BL-202611-0001` (month plus sequence). Allocation is serialised with a SQL
  application lock, so parallel requests never clash.
- Edits carry a row version; saving over someone else's change returns 409 instead of overwriting it.

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
2. Auth and bookings (done)
3. Container movements, tracking and live chat
4. Vessels and voyages
5. Tariffs and detention/demurrage calculator
6. Documents (Bill of Lading PDF, exports)
7. Performance demo
8. Polish and documentation
