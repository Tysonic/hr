#!/usr/bin/env bash
# Adds TyHR to the shared Tysoft Keycloak realm (MyBiz's), so people sign in once for MyBiz and TyHR.
# It only adds TyHR's own clients and roles (tyhr-clients.json); it never changes the realm's settings or MyBiz's
# clients. Users carry one "tenant" attribute (their company) for every app: a company's TyHR code is its MyBiz code.
#
#   deploy/keycloak/setup.sh               # clients tyhr-api (holds the TyHR roles), tyhr-web, tyhr-provisioner
#   deploy/keycloak/setup.sh --dev-users   # also a platform admin and one "demo" company user per TyHR role
#                                          # (passwords -> deploy/keycloak/dev-users.local.txt)
#
# Runs against a Keycloak container on this machine: locally MyBiz's (cd erp/MyBiz && docker compose up -d keycloak).
# On the production server set KEYCLOAK_CONTAINER, KEYCLOAK_URL=https://auth.<domain> and TYHR_DOMAIN (TyHR's address,
# e.g. hr.tysoftltd.com, where tyhr-web may sign in), and copy the printed provisioner secret into TyHR's
# KC_PROVISIONER_SECRET. Safe to re-run: existing roles, passwords and tyhr-web's addresses are kept.
set -euo pipefail

CONTAINER="${KEYCLOAK_CONTAINER:-mybiz-keycloak-1}"
URL="${KEYCLOAK_URL:-http://localhost:8082}"
REALM="${KEYCLOAK_REALM:-MyBiz}"
API_CLIENT=tyhr-api
PLATFORM_ROLE="${PLATFORM_ADMIN_ROLE:-PlatformAdmin}"   # the realm's existing platform-operator role
DIR="$(cd "$(dirname "$0")" && pwd)"
CLIENTS_FILE="$DIR/tyhr-clients.json"
USERS_FILE="$DIR/dev-users.local.txt"
API_PROJECT="$DIR/../../api/src/TyHr.Api"

TYHR_DOMAIN="${TYHR_DOMAIN:-}"
# Outside local development tyhr-web signs in at TyHR's own address only, never at localhost.
case "$URL" in
  http://localhost:*) ;;
  *) [ -n "$TYHR_DOMAIN" ] || { echo "Set TYHR_DOMAIN to TyHR's address, e.g. TYHR_DOMAIN=hr.tysoftltd.com" >&2; exit 2; } ;;
esac
case "$TYHR_DOMAIN" in *[!A-Za-z0-9.-]*) echo "TYHR_DOMAIN is a bare host name, e.g. hr.tysoftltd.com (no https:// or path)." >&2; exit 2 ;; esac

kc() { docker exec -i "$CONTAINER" /opt/keycloak/bin/kcadm.sh "$@" --config /tmp/kcadm.config; }

if [ "$(docker inspect -f '{{.State.Running}}' "$CONTAINER" 2>/dev/null)" != "true" ]; then
  echo "Keycloak container $CONTAINER is not running. Locally: cd ../erp/MyBiz && docker compose up -d keycloak" >&2
  exit 1
fi
for _ in $(seq 90); do curl -sf -o /dev/null "$URL/realms/$REALM" && break; sleep 2; done
curl -sf -o /dev/null "$URL/realms/$REALM" || { echo "Realm $REALM is not reachable at $URL" >&2; exit 1; }

# Log in with the bootstrap admin from the container's own environment (never echoed). Production's has been replaced
# by personal admins: then ask, and kcadm itself prompts for the password.
trap 'docker exec "$CONTAINER" rm -f /tmp/kcadm.config >/dev/null 2>&1 || true' EXIT
if ! docker exec "$CONTAINER" sh -c '/opt/keycloak/bin/kcadm.sh config credentials --config /tmp/kcadm.config \
  --server http://localhost:8080 --realm master \
  --user "$KC_BOOTSTRAP_ADMIN_USERNAME" --password "$KC_BOOTSTRAP_ADMIN_PASSWORD"' >/dev/null 2>&1; then
  echo "The container's bootstrap admin can't sign in. Enter a Keycloak admin of the master realm."
  read -r -p "Username: " admin
  docker exec -it "$CONTAINER" /opt/keycloak/bin/kcadm.sh config credentials --config /tmp/kcadm.config \
    --server http://localhost:8080 --realm master --user "$admin"
fi

client_id() { kc get clients -r "$REALM" -q "clientId=$1" --fields id --format csv --noquotes; }

# Creates what's missing (clients and their roles), then brings each client's settings up to date. Roles are never
# overwritten, which would drop users' role mappings.
echo "Adding TyHR's clients and roles to realm $REALM..."
kc create "realms/$REALM/partialImport" -s ifResourceExists=SKIP -f - < "$CLIENTS_FILE" >/dev/null
for clientId in $(jq -r '.clients[].clientId' "$CLIENTS_FILE"); do
  id="$(client_id "$clientId" </dev/null)"   # looked up first: kc reads stdin, which would swallow the piped JSON
  # tyhr-web keeps the addresses it already has and gains the file's (local development) or TYHR_DOMAIN's.
  current="$(kc get "clients/$id" -r "$REALM" </dev/null)"
  jq --arg c "$clientId" --arg domain "$TYHR_DOMAIN" --argjson current "$current" '
    def addresses($have; $file; $new): if $domain == "" then ($have // []) + $file
      else (($have // []) | map(select(startswith("http://localhost") | not))) + [$new] end | unique;
    .clients[] | select(.clientId == $c) | del(.protocolMappers)
    | if .redirectUris then .redirectUris = addresses($current.redirectUris; .redirectUris; "https://\($domain)/*") else . end
    | if .webOrigins then .webOrigins = addresses($current.webOrigins; .webOrigins; "https://\($domain)") else . end' "$CLIENTS_FILE" \
    | kc update "clients/$id" -r "$REALM" -f -
done
api_id="$(client_id "$API_CLIENT")"
jq -r --arg c "$API_CLIENT" '.roles.client[$c][] | [.name, .description] | @tsv' "$CLIENTS_FILE" \
  | while IFS=$'\t' read -r name description; do
      kc update "clients/$api_id/roles/$name" -r "$REALM" -s "description=$description"
    done

# The API invites company users through the admin REST API with the tyhr-provisioner service account. It looks up
# tyhr-api's roles, so it can read clients as well as manage users.
kc add-roles -r "$REALM" --uusername service-account-tyhr-provisioner --cclientid realm-management \
  --rolename manage-users --rolename view-users --rolename query-users --rolename view-clients --rolename query-clients \
  >/dev/null
provisioner_id="$(client_id tyhr-provisioner)"
# Keycloak 26.8+ only accepts admin REST calls from tokens that carry the realm's admin-API audience, which the
# built-in kc-admin-api client scope adds. Without it every call the provisioner makes gets a 401.
admin_scope_id="$(kc get client-scopes -r "$REALM" | jq -r '.[] | select(.name == "kc-admin-api") | .id')"
if [ -n "$admin_scope_id" ]; then
  kc update "clients/$provisioner_id/default-client-scopes/$admin_scope_id" -r "$REALM" >/dev/null
fi
secret="$(kc get "clients/$provisioner_id/client-secret" -r "$REALM" | jq -r .value)"
if [ "$URL" = "http://localhost:8082" ] && [ -d "$API_PROJECT" ] && command -v dotnet >/dev/null; then
  dotnet user-secrets set "Keycloak:ProvisionerSecret" "$secret" --project "$API_PROJECT" >/dev/null
  echo "Saved the provisioning client secret to the API's user-secrets (Keycloak:ProvisionerSecret)."
else
  echo "Provisioner client secret (set it as TyHR's KC_PROVISIONER_SECRET): $secret"
fi

if [ "${1:-}" = "--dev-users" ]; then
  echo "Creating dev users (passwords in $USERS_FILE)..."
  previous="$( [ -f "$USERS_FILE" ] && cat "$USERS_FILE" || true )"
  : > "$USERS_FILE"
  chmod 600 "$USERS_FILE"
  # user:role:tenant ("-" = no company). platform1 gets the realm's platform role; the others a tyhr-api role.
  for spec in "platform1:$PLATFORM_ROLE:-" hradmin1:hr-admin:demo hrofficer1:hr-officer:demo payroll1:payroll-officer:demo \
              manager1:line-manager:demo employee1:employee:demo; do
    IFS=: read -r user role tenant <<< "$spec"
    first="$(printf '%s' "${role%%-*}" | awk '{ print toupper(substr($0, 1, 1)) substr($0, 2) }')"
    id="$(kc get users -r "$REALM" -q "username=$user" -q exact=true --fields id --format csv --noquotes)"
    if [ -z "$id" ]; then
      kc create users -r "$REALM" -s "username=$user" -s enabled=true -s emailVerified=true \
        -s "firstName=$first" -s "lastName=One" -s "email=$user@tyhr.test" >/dev/null
      id="$(kc get users -r "$REALM" -q "username=$user" -q exact=true --fields id --format csv --noquotes)"
    fi
    if [ "$tenant" != "-" ]; then
      kc update "users/$id" -r "$REALM" -f - <<< "{\"username\":\"$user\",\"email\":\"$user@tyhr.test\",\"firstName\":\"$first\",\"lastName\":\"One\",\"attributes\":{\"tenant\":[\"$tenant\"]}}"
    fi
    password="$(printf '%s\n' "$previous" | awk -v u="$user" '$1 == u { print $2 }')"
    # A new one meets the realm's password policy (MyBiz's needs upper, lower, digit and a special character).
    case "$password" in *[!A-Za-z0-9]*) ;; *) password="$(openssl rand -base64 18 | tr -d '/+=' | cut -c1-12)-Aa1" ;; esac
    docker exec -e PW="$password" "$CONTAINER" sh -c \
      "/opt/keycloak/bin/kcadm.sh set-password --config /tmp/kcadm.config -r $REALM --username $user --new-password \"\$PW\""
    if [ "$role" = "$PLATFORM_ROLE" ]; then
      kc add-roles -r "$REALM" --uusername "$user" --rolename "$role"
    else
      kc add-roles -r "$REALM" --uusername "$user" --cclientid "$API_CLIENT" --rolename "$role"
    fi
    echo "$user  $password  ($role, tenant: $tenant)" >> "$USERS_FILE"
  done
fi

echo "Done. Issuer: $URL/realms/$REALM"
