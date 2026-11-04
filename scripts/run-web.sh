#!/usr/bin/env bash
# Runs the Angular dev server on http://localhost:4200, proxying /api and /health to :5000.
set -euo pipefail
cd "$(dirname "$0")/../web"
# shellcheck disable=SC1091
source ~/.nvm/nvm.sh
nvm use 18 >/dev/null
npx ng serve
