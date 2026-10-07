#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"
args=(--dry-run --Werror)
if [[ "${1:-}" == --fix && $# == 1 ]]; then
    args=(-i)
elif [[ $# != 0 ]]; then
    echo 'Usage: format-native.sh [--fix]' >&2
    exit 2
fi
file_list="$(mktemp)"
trap 'rm -f -- "$file_list"' EXIT
git ls-files --cached --others --exclude-standard -z -- \
    '*.c' '*.cc' '*.cpp' '*.cxx' '*.h' '*.hh' '*.hpp' '*.hxx' '*.inl' > "$file_list"
while IFS= read -r -d '' file; do
    [[ -f "$file" ]] || continue
    clang-format --style="file:$repo_root/.clang-format" "${args[@]}" -- "$file"
done < "$file_list"
