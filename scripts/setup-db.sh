#!/usr/bin/env bash
# Creates or updates the BoxLine schema by applying the EF Core migrations to the database named in the
# API's user-secrets connection string (SQL Server must be running: scripts/start-db.sh or docker compose up -d).
# It only applies pending migrations; it never drops anything. Sample data is seeded by the API on its first start.
set -euo pipefail
cd "$(dirname "$0")/.."

if ! dotnet user-secrets list --project src/BoxLine.Api 2>/dev/null | grep -q "ConnectionStrings:DefaultConnection"; then
  echo "No connection string set. Run, from the repository root:" >&2
  echo "  dotnet user-secrets set --project src/BoxLine.Api \"ConnectionStrings:DefaultConnection\" \"Server=localhost,1433;Database=BoxLine;User Id=sa;Password=<sa-password>;TrustServerCertificate=True;\"" >&2
  exit 1
fi

dotnet ef database update --project src/BoxLine.Infrastructure --startup-project src/BoxLine.Api
echo "Schema is up to date."
