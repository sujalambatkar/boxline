#!/usr/bin/env bash
# Starts the BoxLine SQL Server container (creating nothing, only starting the existing one).
set -euo pipefail

if ! docker info >/dev/null 2>&1; then
  echo "Docker is not running. Start Docker Desktop first." >&2
  exit 1
fi

docker start boxline-sql
echo "SQL Server is starting on localhost:1433 (allow ~15s before the API connects)."
