#!/usr/bin/env bash
#
# Creates the shared application database and its login on FIRST start only. The official image runs the
# scripts in /docker-entrypoint-initdb.d only when the data volume (postgres_data) is empty. To apply changed
# APP_DB_* values afterwards, change them in SQL or reset the volume (README, "Resetting database data").
#
# Every module (Catalog CAT_, Orders ORD_, Notifications NTF_) uses this one database for now and owns only the
# tables with its prefix (docs/database.md). The app login owns the database, so EF Core migrations can create
# tables; it is not a superuser.
set -eo pipefail

for name in APP_DB_NAME APP_DB_USER APP_DB_PASSWORD; do
    if [ -z "${!name}" ]; then
        echo "init.sh: $name is not set. Set it in .env (see .env.example)." >&2
        exit 1
    fi
done

# Values are passed as psql variables and quoted by format(%I / %L), never pasted into the SQL text.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "${POSTGRES_DB:-postgres}" \
    -v app_db="$APP_DB_NAME" -v app_user="$APP_DB_USER" -v app_password="$APP_DB_PASSWORD" <<'EOSQL'
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'app_user', :'app_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'app_user') \gexec

SELECT format('CREATE DATABASE %I OWNER %I', :'app_db', :'app_user')
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = :'app_db') \gexec
EOSQL

echo "init.sh: database '$APP_DB_NAME' and login '$APP_DB_USER' are ready."
