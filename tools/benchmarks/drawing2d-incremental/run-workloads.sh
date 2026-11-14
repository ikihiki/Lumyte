#!/usr/bin/env bash
set -euo pipefail
ROOT=$(git -C "$(dirname "${BASH_SOURCE[0]}")" rev-parse --show-toplevel)
export LUMYTE_ENV_ROOT="${LUMYTE_ENV_ROOT:-$ROOT/artifacts/dev-env}"
export WORKLOAD_REPO_ROOT="$ROOT"
export LUMYTE_WORKLOAD_REVISION=$(git -C "$ROOT" rev-parse HEAD)
# Preserve product JIT/PGO/GC defaults; explicit diagnostic overrides are inherited.
source "$ROOT/tools/setup/activate.sh"
cd "$ROOT"
PROJECT=tools/benchmarks/drawing2d-incremental
command=${1:-list}
if [ "$#" -gt 0 ]; then shift; fi
case "$command" in
  build) dotnet build "$PROJECT" -c Release --nologo; exit ;;
  list) mode=--workload-list ;;
  smoke) mode=--workload-smoke ;;
  cpu) mode=--workload-cpu ;;
  hardware) mode=--workload-hardware; export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json ;;
  *) printf 'Usage: bash run-workloads.sh build|list|smoke|cpu|hardware [--suite 2d|game-ui|editor-ui|all] [options]\n' >&2; exit 2 ;;
esac
dotnet run --project "$PROJECT" -c Release --no-build -- "$mode" "$@"
