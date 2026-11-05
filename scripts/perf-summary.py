#!/usr/bin/env python3
"""Turns the RESULT lines and I/O figures of docs/performance-run.txt into the tables used in docs/PERFORMANCE.md.

Usage: python3 scripts/perf-summary.py [docs/performance-run.txt]
Everything printed comes straight from that file; nothing is computed beyond picking medians and minima/maxima.
"""
import json
import re
import sys

path = sys.argv[1] if len(sys.argv) > 1 else "docs/performance-run.txt"
text = open(path, encoding="utf-8").read()

rows, rss = [], []
lines = text.splitlines()
for i, line in enumerate(lines):
    if line.startswith("RESULT "):
        result = json.loads(line[7:])
        peak = next((float(m.group(1)) for l in lines[i + 1:i + 4] if (m := re.search(r"process peak RSS:\s+(\d+) MB", l))), None)
        rows.append((result, peak))

order = {"naive-ef": 0, "ef-grouped": 1, "sql": 2}
rows.sort(key=lambda r: (order[r[0]["scenario"]], r[0]["range"], not r[0]["reportIndex"]))

print("| Scenario | Request | Page | Index | Warm median | Warm min-max | Cold | Allocated/run | Process peak |")
print("|---|---|---:|---|---:|---|---:|---:|---:|")
for r, peak in rows:
    warm = r["warmMs"]
    cold = f"{r['coldMs']:,.0f}" if r["coldMs"] is not None else "n/a"
    print(f"| {r['scenario']} | {r['range']} | {r['pageRows']} rows | {'present' if r['reportIndex'] else 'absent'} | "
          f"{r['medianMs']:,.0f} | {min(warm):,.0f}-{max(warm):,.0f} | {cold} | {r['allocatedMbPerRun']} MB | {peak:,.0f} MB |")

print()
print("Logical reads of ContainerMovements per report (from the plan sections):")
section = None
for line in lines:
    m = re.match(r"--- execution plan, range=(\w+)", line)
    if m:
        section = [m.group(1), None]
    elif section and (m := re.match(r"Report index : (\w+)", line)):
        section[1] = m.group(1)
    elif section and section[1] and (m := re.match(r"\s+ContainerMovements\s+scans\s+(\d+)\s+logical reads\s+([\d,]+)", line)):
        print(f"  range={section[0]:7s} index {section[1]:8s}: {m.group(2)} logical reads in {m.group(1)} scans")
        section = None
