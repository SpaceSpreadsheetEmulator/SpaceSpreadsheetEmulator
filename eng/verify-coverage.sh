#!/usr/bin/env bash
set -euo pipefail

coverage_file=${1:-artifacts/coverage/Cobertura.xml}
minimum_line=${2:-0.80}
minimum_branch=${3:-0.70}

if [[ ! -f "$coverage_file" ]]; then
    echo "Coverage report not found: $coverage_file" >&2
    exit 2
fi

coverage_element=$(sed -n '/<coverage /{p;q;}' "$coverage_file")
line_rate=$(sed -n 's/.*line-rate="\([^"]*\)".*/\1/p' <<<"$coverage_element")
branch_rate=$(sed -n 's/.*branch-rate="\([^"]*\)".*/\1/p' <<<"$coverage_element")
lines_valid=$(sed -n 's/.*lines-valid="\([^"]*\)".*/\1/p' <<<"$coverage_element")
branches_valid=$(sed -n 's/.*branches-valid="\([^"]*\)".*/\1/p' <<<"$coverage_element")
if [[ -z "$line_rate" || -z "$branch_rate" ||
      -z "$lines_valid" || -z "$branches_valid" ]]; then
    echo "Coverage report is missing required summary attributes: $coverage_file" >&2
    exit 2
fi
if (( lines_valid == 0 || branches_valid == 0 )); then
    echo "Coverage report contains no coverable lines or branches: $coverage_file" >&2
    exit 2
fi

printf 'Merged coverage: line %.2f%%, branch %.2f%%\n' \
    "$(awk -v value="$line_rate" 'BEGIN { print value * 100 }')" \
    "$(awk -v value="$branch_rate" 'BEGIN { print value * 100 }')"

awk -v actual="$line_rate" -v minimum="$minimum_line" \
    'BEGIN { if (actual + 0 < minimum + 0) exit 1 }' || {
    echo "Line coverage $line_rate is below required $minimum_line." >&2
    exit 1
}
awk -v actual="$branch_rate" -v minimum="$minimum_branch" \
    'BEGIN { if (actual + 0 < minimum + 0) exit 1 }' || {
    echo "Branch coverage $branch_rate is below required $minimum_branch." >&2
    exit 1
}
