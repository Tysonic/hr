#!/bin/sh
# Runs once, when the production database volume is first created: TyHR's database and owner.
set -eu
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<SQL
CREATE USER tyhr WITH PASSWORD '$TYHR_DB_PASSWORD';
CREATE DATABASE tyhr OWNER tyhr;
SQL
