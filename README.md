# BoxLine

A mini NVOCC (container shipping line) operations system: bookings, container tracking,
vessels and voyages, detention/demurrage tariffs and documents.

> Work in progress. Phases 0 to 6 are complete (foundation, container master, auth and bookings, movements and live chat, vessels and voyages, detention tariffs and calculator, documents); see the roadmap below.

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

## Detention and demurrage

A **tariff** gives a container type some free days and then a daily rate that steps up in slabs. Slabs use
absolute day numbers: with 5 free days, for example days 6-10 at $40, days 11-15 at $80 and day 16 onwards at
$120. The first slab must start the day after the free days, slabs must be contiguous, only the last is
open-ended, and validity periods of one container type cannot overlap.

How a period is priced (`DetentionCalculator`):

- Days are **calendar days, both the out date and the return date included**, so a same-day return is one day.
- The first *free days* counted days are free; each later day is charged at the rate of the slab containing its day number.
- With **weekends not chargeable**, Saturdays and Sundays are skipped completely: they neither use up free days
  nor cost anything, and day numbering only counts the other days.
- The tariff used is the one valid on the **out date**; a return before the out date is an error.
- For a container, the clock starts at *gate out full* (or *delivered* if no gate out was recorded) and stops at
  *empty returned*. If it has not come back, the charge runs up to today and is shown as ongoing.

The same calculation also exists as the SQL Server stored procedure `usp_CalculateDetention` (created by a
migration). Integration tests run identical cases through both against the real database (every start weekday,
lengths up to 45 days, several tariff shapes, very long stays, and the error cases) and require identical
breakdowns; `POST /api/detention/calculate` accepts `"engine": "Sql"` to run it in the database. Shippers can
price only containers that travelled under their own bookings. Ops manage tariffs at `/tariffs`; everyone uses
the calculator at `/detention`.

## Documents and exports

- **Bill of lading** (`GET /api/bookings/{id}/bill-of-lading`, PDF via QuestPDF Community). A confirmed booking
  gets a clean document dated by the day it was confirmed (`Booking.ConfirmedAt`, which does not change on later
  downloads); a draft gets a preview with a large DRAFT watermark and "Not issued"; a cancelled booking has none
  (409). It prints only what the system holds: shipper, ports, vessel and voyage (or "To be advised"), cargo
  description, the booked container types and quantities, and the container numbers recorded against the booking.
  It states that it is a demonstration document, not a legal instrument. Shippers can only download their own.
- **Bookings export** (`GET /api/bookings/export?format=csv|xlsx`). Takes the same filters as the list and exports
  every matching booking, not just the current page, newest first; shippers only ever get their own bookings.
  Capped at 10,000 rows. CSV is UTF-8 with a byte-order mark (so Excel reads accents) with RFC 4180 quoting, and
  text that would run as a spreadsheet formula (starting with `=`, `+`, `-`, `@`) is prefixed with an apostrophe.
  Excel exports use real date and number cells, a filter row and a frozen header.
- In the app: a "Download bill of lading" button on the booking page and an Export menu on the bookings list.

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
5. Tariffs and detention/demurrage calculator (done)
6. Documents (Bill of Lading PDF, exports) (done)
7. Performance demo
8. Polish and documentation
