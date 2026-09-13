#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIRECTORY="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPOSITORY_ROOT="$(cd "${SCRIPT_DIRECTORY}/.." && pwd)"
SERVER_PROJECT="${REPOSITORY_ROOT}/AI.DocumentAssistant.Server/AI.DocumentAssistant.Server.csproj"
CLIENT_DIRECTORY="${REPOSITORY_ROOT}/ai.documentassistant.client"
DEVELOPMENT_DATABASE="ai_document_assistant"
DEVELOPMENT_USER="$(id -un)"

if [[ "$(uname -s)" != "Darwin" ]]; then
    printf 'This setup script supports macOS only.\n' >&2
    exit 1
fi

for command_name in brew dotnet git; do
    if ! command -v "${command_name}" >/dev/null 2>&1; then
        printf 'Required command not found: %s\n' "${command_name}" >&2
        exit 1
    fi
done

if ! dotnet --list-sdks | grep -Eq '^10\.'; then
    printf '.NET SDK 10 is required.\n' >&2
    exit 1
fi

printf 'Installing verified Homebrew prerequisites...\n'
brew install node postgresql@18 pgvector tesseract tesseract-lang

DETECTED_BREW_PREFIX="$(brew --prefix)"
POSTGRESQL_BIN="$(brew --prefix postgresql@18)/bin"
TESSDATA_DIRECTORY="${DETECTED_BREW_PREFIX}/share/tessdata"

for language in eng ron; do
    if [[ ! -r "${TESSDATA_DIRECTORY}/${language}.traineddata" ]]; then
        printf 'Tesseract language is missing: %s\n' "${language}" >&2
        exit 1
    fi
done

brew services start postgresql@18

for _ in {1..30}; do
    if "${POSTGRESQL_BIN}/pg_isready" -h localhost -p 5432 >/dev/null 2>&1; then
        break
    fi
    sleep 1
done

if ! "${POSTGRESQL_BIN}/pg_isready" -h localhost -p 5432 >/dev/null 2>&1; then
    printf 'PostgreSQL did not become ready on localhost:5432.\n' >&2
    exit 1
fi

if [[ "$("${POSTGRESQL_BIN}/psql" -h localhost -d postgres -Atqc \
    "SELECT 1 FROM pg_database WHERE datname = '${DEVELOPMENT_DATABASE}'")" != "1" ]]; then
    "${POSTGRESQL_BIN}/createdb" -h localhost "${DEVELOPMENT_DATABASE}"
fi

dotnet user-secrets set \
    --project "${SERVER_PROJECT}" \
    "ConnectionStrings:DefaultConnection" \
    "Host=localhost;Port=5432;Database=${DEVELOPMENT_DATABASE};Username=${DEVELOPMENT_USER}"
dotnet user-secrets set \
    --project "${SERVER_PROJECT}" \
    "Ocr:TessDataPath" \
    "${TESSDATA_DIRECTORY}"

cd "${REPOSITORY_ROOT}"
dotnet tool restore
dotnet restore AI.DocumentAssistant.slnx

cd "${CLIENT_DIRECTORY}"
npm ci
npm run build

cd "${REPOSITORY_ROOT}"
dotnet build AI.DocumentAssistant.slnx --no-restore
dotnet ef database update \
    --project AI.DocumentAssistant.Server/AI.DocumentAssistant.Server.csproj \
    --startup-project AI.DocumentAssistant.Server/AI.DocumentAssistant.Server.csproj \
    --no-build
dotnet dev-certs https

printf '\nFirst-time setup completed.\n'
printf 'Still required: store OpenAI:ApiKey with dotnet user-secrets.\n'
printf 'Recommended once: dotnet dev-certs https --trust\n'
