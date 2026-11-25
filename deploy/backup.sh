#!/usr/bin/env bash
# Backs up TyHR's production data: the database, in Tycount's Postgres (pg_dump, custom format), and the uploaded
# files (the "files" volume). Tycount's own backup doesn't include TyHR's database.
# Run on the server from the repository folder, e.g. daily from cron:
#   15 2 * * * cd /opt/tyhr && mkdir -p backups && deploy/backup.sh >> backups/backup.log 2>&1
#
# Settings (environment variables):
#   BACKUP_DIR              where backups go (default: ./backups). Copy them off the server too; see docs/deploy.md.
#   BACKUP_KEEP_DAYS        backups older than this are removed from BACKUP_DIR (default: 14).
#   BACKUP_PASSPHRASE_FILE  if set, each backup is encrypted with the passphrase in this file (openssl AES-256).
#   TYCOUNT_DIR             Tycount's checkout, whose Postgres holds the database (default: /opt/mybiz/MyBiz).
#   TYHR_COMPOSE_FILE, ENV_FILE  default: docker-compose.prod.yml and .env.prod.
#
# Not included, on purpose: .env.prod. It holds TYHR_SECRETS_KEY, without which the encrypted ID and bank numbers in
# the database can't be read. Keep a copy of it somewhere safe and separate from these backups.
set -euo pipefail
umask 077   # backups hold everyone's pay: only this user may read them, from the first byte

TYHR_COMPOSE_FILE=${TYHR_COMPOSE_FILE:-docker-compose.prod.yml}
ENV_FILE=${ENV_FILE:-.env.prod}
TYCOUNT_DIR=${TYCOUNT_DIR:-/opt/mybiz/MyBiz}
BACKUP_DIR=${BACKUP_DIR:-./backups}
BACKUP_KEEP_DAYS=${BACKUP_KEEP_DAYS:-14}
compose() { docker compose -f "$TYHR_COMPOSE_FILE" --env-file "$ENV_FILE" "$@"; }
# In Tycount's Postgres container (its own .env sets its COMPOSE_FILE).
pg() { (cd "$TYCOUNT_DIR" && env -u COMPOSE_FILE -u COMPOSE_PROJECT_NAME docker compose exec -T postgres "$@"); }
log() { echo "$(date '+%F %T') $*"; }

if [ -n "${BACKUP_PASSPHRASE_FILE:-}" ] && [ ! -s "$BACKUP_PASSPHRASE_FILE" ]; then
  echo "BACKUP_PASSPHRASE_FILE ($BACKUP_PASSPHRASE_FILE) is missing or empty." >&2; exit 2
fi
mkdir -p "$BACKUP_DIR"
stamp=$(date -u +%Y%m%d-%H%M%S)
db="$BACKUP_DIR/tyhr-db-$stamp.dump"
files="$BACKUP_DIR/tyhr-files-$stamp.tar.gz"
# Unfinished backups are called .part-*, so off-server copies of tyhr-* never pick up a partial or unencrypted one.
part_db="$BACKUP_DIR/.part-db-$stamp"; part_files="$BACKUP_DIR/.part-files-$stamp"
trap 'rm -f "$part_db" "$part_files" "$part_db.enc" "$part_files.enc"' EXIT

log "Backing up the database"
pg pg_dump -U postgres -d tyhr --format=custom --compress=6 > "$part_db"
# A dump that pg_restore can't list is no backup: check before keeping it.
pg pg_restore --list < "$part_db" > /dev/null

log "Backing up uploaded files"
compose run --rm --no-deps -T --entrypoint tar tyhr-api -C /files -czf - . > "$part_files"
tar -tzf "$part_files" > /dev/null

if [ -n "${BACKUP_PASSPHRASE_FILE:-}" ]; then
  for f in "$part_db" "$part_files"; do
    openssl enc -aes-256-cbc -pbkdf2 -iter 200000 -salt -pass "file:$BACKUP_PASSPHRASE_FILE" -in "$f" -out "$f.enc"
  done
  db="$db.enc"; files="$files.enc"
  mv "$part_db.enc" "$db"; mv "$part_files.enc" "$files"
else
  mv "$part_db" "$db"; mv "$part_files" "$files"
fi
log "Saved $db ($(du -h "$db" | cut -f1)) and $files ($(du -h "$files" | cut -f1))"

find "$BACKUP_DIR" -maxdepth 1 -name 'tyhr-*' -type f -mtime +"$BACKUP_KEEP_DAYS" -print -delete | sed 's/^/Removed old backup /'
log "Done"
