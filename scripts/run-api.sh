#!/usr/bin/env bash
# Runs the API on http://localhost:5000 (Swagger at /swagger).
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project src/BoxLine.Api --launch-profile http
