# BoxLine (folder: econshipproject)

Mini NVOCC (container shipping line) portfolio app. Built in phases 0-8 with Claude Code; all committed. Phase 9 (LLM booking-email extractor) was deliberately skipped: this is a demo app, do not add LLM features unless asked.

The folder was renamed from `angularproject` to `econshipproject` after the build; the GitHub repo is still `sujalambatkar/boxline` (private). Nothing in the code depends on the folder name.

## Stack
- Back end: .NET 10, ASP.NET Core Web API, EF Core 10 (SQL Server), Dapper, QuestPDF, MiniExcel, SignalR, JWT.
- Front end: Angular 15 (NgModules, Material 15), Node 18 via nvm. Dev proxy forwards `/api` to the API.
- Database: SQL Server 2022 in Docker (container `boxline-sql`, port 1433; amd64 image under emulation on Apple Silicon). Do not run SQL Server natively.
- Tests: xUnit (~690) and Jasmine/Karma (366).

## Layout
- `src/BoxLine.Core`: entities and business rules, no EF/ASP.NET references.
- `src/BoxLine.Infrastructure`: EF context, migrations, repositories, Dapper report, PDF/Excel.
- `src/BoxLine.Api`: controllers, JWT, SignalR `ChatHub`, Swagger.
- `tests/BoxLine.Tests`: integration tests run against a separate `BoxLineTests` database.
- `tools/BoxLine.PerfTool`: seeds 500,000 movements into a separate `BoxLinePerf` database and benchmarks the utilisation report (`scripts/perf.sh`).
- `web/`: Angular app. `docs/`: PERFORMANCE.md and screenshots. `scripts/`: start-db, run-api, run-web, test, setup-db, perf.

## Commands
- `scripts/start-db.sh` (or `docker compose up -d` with `MSSQL_SA_PASSWORD` set), `scripts/run-api.sh` (:5000), `scripts/run-web.sh` (:4200), `scripts/test.sh`, `scripts/setup-db.sh` (applies migrations).
- Check formatting with `dotnet format BoxLine.sln --verify-no-changes`.

## Rules that were agreed and must be kept
- Secrets only in `dotnet user-secrets` (connection string, `Jwt:Secret`); `appsettings.Example.json` holds placeholders. Never commit secrets.
- No global tool installs; no sudo or system changes without asking.
- Ask before any destructive action (deleting folders, dropping a database outside migrations). `BoxLinePerf` is the only database that may be dropped and reseeded, and only with permission.
- Free tools only. XML docs on public service methods. No dead code or TODOs.
- Conventional commits, ending with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- VS Code must use the project's TypeScript 4.9 (`.vscode/settings.json`); the red deprecation warnings in `web/tsconfig.json` come from VS Code's newer built-in TypeScript. Do not add `ignoreDeprecations` to tsconfig.

## Design facts worth knowing
- Booking numbers (`BL-YYYYMM-NNNN`) and container movement recording are serialised with `sp_getapplock`.
- State machines: booking (Draft, Confirmed, Cancelled), movement cycle, voyage. Bookings use a row version (409 on conflict).
- Detention calculator exists in C# and as stored procedure `usp_CalculateDetention`, with parity tests.
- Utilisation report: `usp_UtilisationByMonthAndPort` via Dapper over covering index `IX_ContainerMovements_Report` (persisted computed column `OccurredMonth`). Naive and EF-grouped versions are kept only as baseline and test oracle. Measured results and limits: `docs/PERFORMANCE.md`.
- Integration tests create unique data and must stay re-runnable against `BoxLineTests`.

## State of the dev data
The dev `BoxLine` database holds seeded reference data (7 ports, 6 container types, 200 containers, 3 vessels with sailings, two starter accounts `ops@boxline.test` and `shipper@boxline.test`) plus three demo bookings and eight movements created for the README screenshots. Account passwords were printed once at first API start and are not stored in the repo.
