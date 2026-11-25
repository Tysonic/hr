#!/usr/bin/env bash
# Restores TyHR from backups made by deploy/backup.sh, REPLACING TyHR's current database and uploaded files. Only
# the "tyhr" database in Tycount's Postgres is touched, never Tycount's or the website's.
#   deploy/restore.sh backups/tyhr-db-<stamp>.dump backups/tyhr-files-<stamp>.tar.gz
# Encrypted backups (.enc) need BACKUP_PASSPHRASE_FILE. The API is stopped while restoring and started afterwards; it
# applies any newer migrations on start. .env.prod must hold the same TYHR_SECRETS_KEY as when the backup was made.
set -euo pipefail

TYHR_COMPOSE_FILE=${TYHR_COMPOSE_FILE:-docker-compose.prod.yml}
ENV_FILE=${ENV_FILE:-.env.prod}
TYCOUNT_DIR=${TYCOUNT_DIR:-/opt/mybiz/MyBiz}
compose() { docker compose -f "$TYHR_COMPOSE_FILE" --env-file "$ENV_FILE" "$@"; }
# In Tycount's Postgres container (its own .env sets its COMPOSE_FILE).
pg() { (cd "$TYCOUNT_DIR" && env -u COMPOSE_FILE -u COMPOSE_PROJECT_NAME docker compose exec -T postgres "$@"); }
log() { echo "$(date '+%F %T') $*"; }

[ $# -eq 2 ] || { echo "Usage: $0 <database .dump[.enc]> <files .tar.gz[.enc]>" >&2; exit 2; }
db=$1; files=$2
for f in "$db" "$files"; do [ -f "$f" ] || { echo "No such file: $f" >&2; exit 2; }; done

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
plain() { # the backup, decrypted if need be
  if [[ $1 == *.enc ]]; then
    [ -n "${BACKUP_PASSPHRASE_FILE:-}" ] || { echo "$1 is encrypted: set BACKUP_PASSPHRASE_FILE." >&2; exit 2; }
    openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -pass "file:$BACKUP_PASSPHRASE_FILE" -in "$1" -out "$work/$(basename "${1%.enc}")"
    echo "$work/$(basename "${1%.enc}")"
  else echo "$1"; fi
}
db=$(plain "$db"); files=$(plain "$files")
pg pg_restore --list < "$db" > /dev/null
tar -tzf "$files" > /dev/null

[ "$(pg psql -U postgres -qAtc "SELECT 1 FROM pg_roles WHERE rolname = 'tyhr'" < /dev/null)" = "1" ] \
  || { echo "There's no tyhr login in Tycount's Postgres: run deploy/postgres/create-db.sh first." >&2; exit 2; }

if [ "${RESTORE_CONFIRM:-}" != "yes" ]; then
  echo "This replaces ALL of TyHR's current data (every company) with the backup. It can't be undone."
  read -r -p "Type 'restore' to continue: " answer
  [ "$answer" = "restore" ] || { echo "Stopped; nothing changed."; exit 1; }
fi

log "Stopping the API"
compose stop tyhr-api
log "Restoring the database"
pg psql -U postgres -v ON_ERROR_STOP=1 -q \
  -c 'DROP DATABASE IF EXISTS tyhr WITH (FORCE)' -c 'CREATE DATABASE tyhr OWNER tyhr' -c 'REVOKE ALL ON DATABASE tyhr FROM PUBLIC' < /dev/null
pg pg_restore -U postgres -d tyhr --no-owner --role=tyhr --exit-on-error < "$db"
log "Restoring uploaded files"
compose run --rm --no-deps -T --entrypoint sh tyhr-api -c 'find /files -mindepth 1 -delete && tar -C /files -xzf -' < "$files"
log "Starting the API"
compose up -d tyhr-api
log "Done. Check https://<your domain>/api/health, then sign in and look around."
