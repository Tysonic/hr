#!/usr/bin/env bash
# One-time set-up on the production server: creates TyHR's login and database (both "tyhr") in Tycount's Postgres, with
# TYHR_DB_PASSWORD from .env.prod. Nothing else in that Postgres is touched. Safe to run again: it creates what's
# missing and sets the login's password to the one in .env.prod.
#   deploy/postgres/create-db.sh
# Settings: TYCOUNT_DIR, Tycount's checkout (default /opt/tycount/Tycount); ENV_FILE (default .env.prod).
set -euo pipefail
cd "$(dirname "$0")/../.."
TYCOUNT_DIR="${TYCOUNT_DIR:-/opt/tycount/Tycount}"
ENV_FILE="${ENV_FILE:-.env.prod}"
[ -f "$ENV_FILE" ] || { echo "No $ENV_FILE in $(pwd)." >&2; exit 1; }
DB_PASSWORD="$(sed -n 's/^TYHR_DB_PASSWORD=//p' "$ENV_FILE" | tail -1)"
# Hex only (openssl rand -hex 24), so it means the same in .env.prod, SQL and the connection string.
[[ "$DB_PASSWORD" =~ ^[0-9a-f]{32,}$ ]] || { echo "Set TYHR_DB_PASSWORD in $ENV_FILE first, to the output of: openssl rand -hex 24" >&2; exit 1; }
export DB_PASSWORD

cd "$TYCOUNT_DIR"
# Passed as an environment variable, not on the command line, so process lists can't show it.
env -u COMPOSE_FILE -u COMPOSE_PROJECT_NAME docker compose exec -T -e DB_PASSWORD postgres sh -c \
  'psql -qAt -v ON_ERROR_STOP=1 -U postgres -d postgres -v password="$DB_PASSWORD"' <<'SQL'
SELECT 'CREATE ROLE tyhr' WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'tyhr') \gexec
ALTER ROLE tyhr LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD :'password';
SELECT 'CREATE DATABASE tyhr OWNER tyhr' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'tyhr') \gexec
REVOKE ALL ON DATABASE tyhr FROM PUBLIC;
SQL
echo "The tyhr login and database are ready in Tycount's Postgres."
