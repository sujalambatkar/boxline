#!/usr/bin/env bash
# Reproduces the measurements in docs/PERFORMANCE.md on the BoxLinePerf database.
#
#   scripts/perf.sh seed      create and fill BoxLinePerf (25,000 containers, 500,000 movements); never run on startup
#   scripts/perf.sh measure   run the whole benchmark matrix and print the raw output
#
# macOS only: it uses sysctl and /usr/bin/time -l (which reports each process's peak memory as the OS saw it).
# The tool refuses to touch any database whose name does not contain "Perf".
set -euo pipefail
cd "$(dirname "$0")/.."

TOOL="tools/BoxLine.PerfTool/bin/Release/net10.0/BoxLine.PerfTool.dll"
dotnet build tools/BoxLine.PerfTool -c Release -v q >/dev/null

# Runs the tool and turns the OS's resource summary into one readable line.
run() {
  /usr/bin/time -l dotnet "$TOOL" "$@" 2>&1 | awk '
    /maximum resident set size/ { printf "  process peak RSS: %10.0f MB (measured by the OS)\n", $1 / 1048576; next }
    /^ +[0-9.]+ +real/ { next }
    /^ +[0-9]+ +[a-z]/ { next }
    { print }'
}

case "${1:-measure}" in
  seed)
    run seed
    ;;
  measure)
    echo "=== Environment"
    echo "date             : $(date -u '+%Y-%m-%d %H:%M UTC')"
    echo "machine          : $(sysctl -n hw.model), $(sysctl -n machdep.cpu.brand_string), $(sysctl -n hw.memsize | awk '{printf "%.0f GB RAM", $1/1073741824}')"
    echo "OS               : $(sw_vers -productName) $(sw_vers -productVersion)"
    echo ".NET SDK         : $(dotnet --version)"
    echo "Docker           : $(docker --version)"
    echo "Docker resources : $(docker info --format '{{.NCPU}} {{.MemTotal}}' 2>/dev/null | awk '{printf "%s CPUs, %.1f GB memory", $1, $2/1073741824}')"
    echo "Other containers : $(($(docker ps -q | wc -l) - 1)) besides boxline-sql were running (they share the machine)"
    PW=$(docker inspect boxline-sql --format '{{range .Config.Env}}{{println .}}{{end}}' | sed -n 's/^MSSQL_SA_PASSWORD=//p')
    echo "SQL Server       : $(docker exec boxline-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -h -1 -W -Q "SET NOCOUNT ON; SELECT REPLACE(SUBSTRING(@@VERSION, 1, CHARINDEX(CHAR(10), @@VERSION) - 1), CHAR(9), ' ')")"
    echo "SQL Server image : $(docker image inspect mcr.microsoft.com/mssql/server:2022-latest --format '{{.Architecture}}') image on this $(uname -m) Mac (x86_64 on arm64 means Rosetta emulation)"
    unset PW
    echo
    echo "=== Data"
    run info

    echo; echo "=== A. Report index ABSENT"
    run indexes drop
    for range in last12 all; do echo; echo "--- naive-ef, range=$range"; run benchmark --scenario naive-ef --range "$range" --runs 3 --cold; done
    for range in last12 all; do echo; echo "--- ef-grouped, range=$range"; run benchmark --scenario ef-grouped --range "$range" --runs 5 --cold; done
    for range in last12 all wide; do echo; echo "--- sql, range=$range"; run benchmark --scenario sql --range "$range" --runs 5 --cold; done
    for range in last12 all wide; do echo; echo "--- execution plan, range=$range"; run explain --range "$range"; done

    echo; echo "=== B. Report index PRESENT"
    run indexes create
    echo; echo "--- naive-ef, range=last12 (control: it reads everything either way)"; run benchmark --scenario naive-ef --range last12 --runs 3 --cold
    for range in last12 all; do echo; echo "--- ef-grouped, range=$range"; run benchmark --scenario ef-grouped --range "$range" --runs 5 --cold; done
    for range in last12 all wide; do echo; echo "--- sql, range=$range"; run benchmark --scenario sql --range "$range" --runs 5 --cold; done
    echo; echo "--- sql, range=all, whole report on one page (498 rows instead of 20)"; run benchmark --scenario sql --range all --runs 5 --page-size 500
    for range in last12 all wide; do echo; echo "--- execution plan, range=$range"; run explain --range "$range"; done
    echo; echo "=== Storage after the index"
    run info
    ;;
  *)
    echo "usage: scripts/perf.sh seed|measure" >&2
    exit 1
    ;;
esac
