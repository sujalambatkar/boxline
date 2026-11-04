#!/usr/bin/env bash
# Runs the .NET and Angular test suites. Requires the SQL Server container to be running.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet test BoxLine.sln
cd web
# shellcheck disable=SC1091
source ~/.nvm/nvm.sh
nvm use 18 >/dev/null
npx ng test --watch=false --browsers=ChromeHeadless
