#!/usr/bin/env bash
set -euo pipefail

infrastructure_directory="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
repository_directory="$(CDPATH= cd -- "${infrastructure_directory}/.." && pwd)"
compose_project="space-spreadsheet-emulator-development"
environment_file="${infrastructure_directory}/.env"
if [[ ! -f "${environment_file}" ]]; then
    environment_file="${infrastructure_directory}/.env.example"
fi

set -a
# shellcheck disable=SC1090
source "${environment_file}"
set +a

: "${SSE_POSTGRES_DATABASE:=space_spreadsheet_emulator}"
: "${SSE_POSTGRES_USER:=sse}"
: "${SSE_POSTGRES_PASSWORD:=local-development-only}"
: "${SSE_POSTGRES_PORT:=5432}"

if command -v podman >/dev/null 2>&1 && podman compose version >/dev/null 2>&1; then
    compose_command=(podman compose)
elif command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
    compose_command=(docker compose)
else
    echo "Podman Compose or Docker Compose is required." >&2
    exit 1
fi

compose() {
    "${compose_command[@]}" \
        --project-name "${compose_project}" \
        --env-file "${environment_file}" \
        --file "${infrastructure_directory}/compose.yaml" \
        "$@"
}

wait_until_ready() {
    for _ in {1..60}; do
        if compose exec -T postgres \
            pg_isready -U "${SSE_POSTGRES_USER}" -d "${SSE_POSTGRES_DATABASE}" \
            >/dev/null 2>&1; then
            return
        fi

        sleep 1
    done

    echo "PostgreSQL did not become ready within 60 seconds." >&2
    compose logs postgres >&2
    exit 1
}

migrate() {
    (
        cd "${repository_directory}"
        dotnet tool restore
        dotnet ef database update \
            --project src/SpaceSpreadsheetEmulator.Persistence \
            --startup-project src/SpaceSpreadsheetEmulator.Persistence \
            -- \
            --environment CliTest
    )
}

command="${1:-}"
case "${command}" in
    up)
        compose up --detach postgres
        wait_until_ready
        ;;
    down)
        compose down --remove-orphans
        ;;
    migrate)
        migrate
        ;;
    status)
        compose ps
        (
            cd "${repository_directory}"
            dotnet tool restore
            dotnet ef migrations list \
                --project src/SpaceSpreadsheetEmulator.Persistence \
                --startup-project src/SpaceSpreadsheetEmulator.Persistence \
                -- \
                --environment CliTest
        )
        ;;
    reset)
        compose down --volumes --remove-orphans
        compose up --detach postgres
        wait_until_ready
        migrate
        ;;
    *)
        echo "Usage: $0 {up|down|migrate|status|reset}" >&2
        exit 2
        ;;
esac
