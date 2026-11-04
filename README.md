# BoxLine

A mini NVOCC (container shipping line) operations system: bookings, container tracking,
vessels and voyages, detention/demurrage tariffs and documents.

> Work in progress. Phases 0 to 4 are complete (foundation, container master, auth and bookings, movements and live chat, vessels and voyages); see the roadmap below.

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

## Container movements and tracking

Ops record the events of a container's life; they must follow the cycle and can never go back in time:

`GateOutEmpty -> Stuffed -> GateInFull -> LoadedOnVessel -> Discharged -> GateOutFull -> DeliveredToConsignee -> EmptyReturned`

then a new cycle starts with `GateOutEmpty`. An out-of-order event is a 409, a backdated or future event
a 400. An event may be linked to a **confirmed** booking. Recording is atomic per container (an
application lock), so two operators cannot both append "the next" event.

Tracking works by container number or booking number (`/tracking?q=...`). A shipper sees only the
events linked to their own bookings; anything else looks exactly like a container that does not exist.
A container that has history cannot be deleted.

## Live chat

Each booking has a conversation between its shipper and Ops over SignalR (`/hubs/chat`). The JWT is sent
as a query value because browsers cannot set headers on a WebSocket. A connection receives a booking's
messages only after joining its group, joining and sending both re-check access, and messages are stored
(`GET /api/bookings/{id}/messages`). The Angular panel reloads history and rejoins after a reconnect.

## Vessels, voyages and sailings

- **IMO numbers** are seven digits; multiply the first six by 7, 6, 5, 4, 3, 2 and the last digit of the sum
  must equal the seventh (for example 9074729). `IMO 9074-729` style input is accepted and normalized.
- A **voyage** is one sailing of a vessel through a rotation of ports. The sequence must strictly increase, a
  port can appear only once, the first call needs an ETD and the last an ETA (intermediate calls need both),
  ETD cannot precede ETA, and each call must arrive after the previous one departs.
- Voyage status: `Scheduled -> Underway -> Completed`, and `Cancelled` from Scheduled or Underway. The rotation
  is editable only while Scheduled. Actual arrival/departure times are recorded while Underway, in rotation
  order. Completing needs the arrival at the last port; cancelling is blocked while active bookings are on it.
- **Schedule search** (`GET /api/schedules`) lists open sailings where the loading port comes before the
  discharge port, inside a departure window (default the next 90 days). An underway voyage stops taking
  bookings from a port once it has left it.
- A **booking** can sail on a voyage only if that voyage calls at its loading port before its discharge port.
  Shippers pick a sailing while drafting; Ops can assign or change it on a confirmed booking. A rotation edit
  that would strand an active booking is refused.

In Development, three fictional vessels with two sailings each on the Shanghai to Hamburg lane are seeded.

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
3. Container movements, tracking and live chat (done)
4. Vessels and voyages (done)
5. Tariffs and detention/demurrage calculator
6. Documents (Bill of Lading PDF, exports)
7. Performance demo
8. Polish and documentation
