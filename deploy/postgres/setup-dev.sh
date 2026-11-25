#!/usr/bin/env bash
# Local development: TyHR's role and database on the shared Tysoft Postgres (one server for every product).
#   deploy/postgres/setup-dev.sh
#
# Creates only what's missing: a "tyhr" login that isn't a superuser and a "tyhr" database it owns. Nothing else on the
# server is touched, and an existing tyhr database keeps its data. The login gets a new password on each run, saved
# in the API's user-secrets (ConnectionStrings:Default).
#
# Settings: PG_CONTAINER (default postgres_container), PG_PORT (its port on this machine, default 5432).
set -euo pipefail

CONTAINER="${PG_CONTAINER:-postgres_container}"
PORT="${PG_PORT:-5432}"
DIR="$(cd "$(dirname "$0")" && pwd)"
API_PROJECT="$DIR/../../api/src/TyHr.Api"

if [ "$(docker inspect -f '{{.State.Running}}' "$CONTAINER" 2>/dev/null)" != "true" ]; then
  echo "Postgres container $CONTAINER is not running. Start it, or set PG_CONTAINER." >&2
  exit 1
fi
# The server's own superuser, over the container's local socket.
psql() { docker exec -i "$CONTAINER" sh -c 'exec psql -v ON_ERROR_STOP=1 -qAt -U "${POSTGRES_USER:-postgres}" -d postgres "$@"' psql "$@"; }

if [ "$(psql -c "select rolsuper from pg_roles where rolname = 'tyhr'" </dev/null)" = "t" ]; then
  echo "A superuser called tyhr already exists on $CONTAINER; not touching it." >&2
  exit 1
fi
password="$(openssl rand -hex 24)"
# On stdin, so the password isn't in any process list.
psql <<SQL
SELECT 'CREATE ROLE tyhr' WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'tyhr') \gexec
ALTER ROLE tyhr LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '$password';
SELECT 'CREATE DATABASE tyhr OWNER tyhr' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'tyhr') \gexec
REVOKE ALL ON DATABASE tyhr FROM PUBLIC;
SQL
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=$PORT;Database=tyhr;Username=tyhr;Password=$password" \
  --project "$API_PROJECT" >/dev/null
echo "The tyhr database is ready on $CONTAINER (port $PORT); the API's user-secrets have its password."
