# Deploying TyHR

TyHR goes on **Tycount's production server**, beside Tycount and the company website, and borrows three of
Tycount's services. This guide covers the first deploy, updates, backups and restores, monitoring, and a security
checklist. It assumes Tycount is already running as in its own guide, `/opt/tycount/Tycount/DEPLOY-PRODUCTION.md` (server
preparation, firewall, Docker, DNS and mail come from there).

## What runs where

| Service | Whose | What it does for TyHR |
|---|---|---|
| `tyhr-api` | TyHR | The .NET API. Applies database migrations when it starts. Nothing published. |
| `tyhr-web` | TyHR | The React app as static files. Nothing published. |
| Caddy | Tycount | The only public entrance: serves `https://<TYHR_DOMAIN>` from `deploy/caddy/sites/tyhr.caddy`. |
| Keycloak | Tycount | Sign-in (realm `tysoft-apps`), shared so one sign-in covers both apps. |
| Postgres 17 | Tycount | Holds the `tyhr` database, owned by its own `tyhr` login. |

They meet on the `tyhr-edge` Docker network, which TyHR's stack creates and `deploy/tycount.override.yml` connects
Tycount's three services to. TyHR's own data is the `tyhr` database and the `files` volume (documents and CVs). Tycount's
backup covers **neither**; TyHR's does (see [Backups](#backups)).

Every company uses `https://<TYHR_DOMAIN>`. Which company a user belongs to comes from their sign-in, not from the
address.

What sharing means:
- Tycount's Postgres being down, upgraded or moved takes TyHR with it. Include TyHR in those plans.
- Postgres allows about 100 connections in all by default. TyHR uses at most 30.
- A restore of TyHR replaces only the `tyhr` database. A restore of Tycount doesn't touch it.
- A TyHR release never restarts Tycount.

## Server

Tycount's guide asks for 2 vCPUs and 8 GB of RAM. TyHR adds an API and a small web server; check with `free -h` after the
first start, and build at a quiet time. It also needs `jq` and `openssl`. Docker Compose must be 2.24.4 or later, as for
Tycount.

## First deploy

1. **DNS.** In Cloudflare, point `TYHR_DOMAIN` (e.g. `hr.tysoftltd.com`, an A record) at the server, **DNS only** (grey
   cloud). If it's proxied, Caddy sees Cloudflare's addresses, and the careers page's per-visitor limits count many
   applicants as one.
2. **Code.** Clone the repository to `/opt/tyhr`. Tycount's Caddyfile must have the line
   `import /etc/caddy/sites/*.caddy` (it's in Tycount's repository from the website release on).
3. **Settings.** Create `/opt/tyhr/.env.prod`, with `chmod 600`:

   | Setting | Required | What it is |
   |---|---|---|
   | `TYHR_DOMAIN` | yes | e.g. `hr.tysoftltd.com` |
   | `TYHR_DB_PASSWORD` | yes | password of the `tyhr` database login: `openssl rand -hex 24` (hex only) |
   | `TYHR_SECRETS_KEY` | yes | encrypts ID, bank and mobile-money numbers: `openssl rand -base64 32`. **Losing it makes those fields unreadable.** Keep a copy somewhere safe, apart from the backups. |
   | `AUTH_URL` | yes | Keycloak as browsers see it, e.g. `https://auth.tycount.tysoftltd.com` |
   | `KC_PROVISIONER_SECRET` | yes | from step 5 |
   | `PLATFORM_TENANT` | no | Tycount's operator company. Only its `PlatformAdmin`s become TyHR platform admins. |
   | `SMTP_HOST`, `SMTP_PORT`, `SMTP_SECURITY`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `MAIL_FROM` | no | outgoing mail (payslips, reminders, offers). Without `SMTP_HOST` nothing is sent, and reminders wait. `SMTP_SECURITY` is `StartTls` (587), `Ssl` (465) or `None`. |

4. **Database.** `deploy/postgres/create-db.sh` creates the login and database in Tycount's Postgres. It's safe to re-run,
   and touches nothing else there.

   Recommended, if not already done for the website: by Postgres's default any login may connect to any database (it
   can't read the tables, but it can list them). Close Tycount's to other logins; their owners keep access:

   ```sh
   cd /opt/tycount/Tycount
   docker compose exec -T postgres psql -U postgres -c 'REVOKE CONNECT ON DATABASE tycount_db, efris_db, keycloak FROM PUBLIC'
   ```

5. **Keycloak.** From `/opt/tyhr`:

   ```sh
   KEYCLOAK_CONTAINER=$(cd /opt/tycount/Tycount && docker compose ps -q keycloak) \
     KEYCLOAK_URL=https://auth.<Tycount's domain> TYHR_DOMAIN=hr.tysoftltd.com deploy/keycloak/setup.sh
   ```

   - Tycount's bootstrap admin is gone, so it asks for your own Keycloak admin sign-in.
   - It adds TyHR's clients and roles to the tysoft-apps realm, lets `tyhr-web` sign in at `https://<TYHR_DOMAIN>`, and prints
     the provisioner secret for `.env.prod`.
   - It's safe to re-run, and keeps the addresses already registered. Don't change the realm by hand.
   - Check that **Direct access grants are off** on `tyhr-web`.
   - Tycount's "own company only" sign-in steps check only the clients listed in their settings (`tycount-web`). Don't add
     `tyhr-web` there: TyHR has one address for every company, so everyone would be refused.
6. **Start TyHR** (this creates `tyhr-edge`):

   ```sh
   cd /opt/tyhr && docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build
   ```

   Until step 7 the API can't reach Postgres, so it keeps restarting. That's expected.
7. **Connect Tycount's services**, at a quiet time and **after a Tycount backup**: this recreates its Caddy, Keycloak and
   Postgres, and Tycount is unavailable for about a minute. In `/opt/tycount/Tycount/.env`, add TyHR's override to the
   **end** of `COMPOSE_FILE`, after the website's, and set `TYHR_DOMAIN` if it isn't `hr.tysoftltd.com`:

   ```sh
   COMPOSE_FILE=docker-compose.yml:docker-compose.prod.yml:/opt/tysoft-website/deploy/tycount-caddy.override.yml:/opt/tyhr/deploy/tycount.override.yml
   ```

   Check first that Caddy will get TyHR's sites folder, with the website's beside it, and then apply:

   ```sh
   cd /opt/tycount/Tycount
   docker compose config | grep -A1 'source: /opt/ty'   # .../tyhr/deploy/caddy/sites and .../tysoft-website/deploy/caddy
   docker compose up -d caddy keycloak postgres
   docker compose logs -f caddy                         # "certificate obtained successfully" for TyHR's domain
   ```

   The order matters because only one folder can be mounted as Caddy's sites. TyHR's takes the website's place and
   imports the website's. In the wrong order the website still works, but TyHR isn't served. Without the website on the
   server, leave its override out.
8. **Check it:**
   - `curl https://<TYHR_DOMAIN>/api/health` should return `{"status":"ok"}`.
   - Tycount (a company address and sign-in) and the website still load.
9. **First company.** Sign in as a platform admin, go to Platform → Companies, and add the company with its HR admin's
   email. The HR admin is invited by email to set a password, unless they already have a Tycount sign-in in the same company.
10. **Backups.** Set them up now, before anyone enters data (next section).

If TyHR is ever removed from the server, take its override out of Tycount's `COMPOSE_FILE` (and recreate the three
services) before removing its stack.

## Backups

`deploy/backup.sh` saves the `tyhr` database in Tycount's Postgres (`pg_dump`, checked with `pg_restore --list` before it's kept) and the uploaded
files into `backups/`, both or neither. It removes backups older than `BACKUP_KEEP_DAYS` (default 14).

1. Make a passphrase file, keep a copy of it with `.env.prod`'s copy, and run the backup daily from cron:

   ```sh
   openssl rand -base64 32 > /root/tyhr-backup.pass && chmod 600 /root/tyhr-backup.pass
   # crontab -e   (as a user who may run docker; Tycount's runs at 02:00)
   15 2 * * * cd /opt/tyhr && mkdir -p backups && BACKUP_PASSPHRASE_FILE=/root/tyhr-backup.pass deploy/backup.sh >> backups/backup.log 2>&1
   ```

2. Copy backups off the server, because a backup on the same disk doesn't survive losing the server. Keep them
   **in Kenya or Uganda**, like Tycount's (its guide, section 12). For example, with rclone, after the backup:

   ```sh
   30 2 * * * rclone copy /opt/tyhr/backups remote:tyhr-backups --include 'tyhr-*'
   ```

   Keep 30 days or more off the server.

3. **`.env.prod` is not in the backups, on purpose.** Store it (and the passphrase) in a password manager or a sealed
   place. A database backup without `TYHR_SECRETS_KEY` restores, but ID and bank numbers come back unreadable.

4. Test a restore every quarter, on a spare server or a copy of the stack, not on production.

## Restoring

```sh
cd /opt/tyhr
BACKUP_PASSPHRASE_FILE=/root/tyhr-backup.pass deploy/restore.sh backups/tyhr-db-<stamp>.dump.enc backups/tyhr-files-<stamp>.tar.gz.enc
```

The script:
1. Decrypts the backups and checks that they're readable.
2. Asks you to type `restore`.
3. Stops the API, replaces the `tyhr` database (and no other) and the uploaded files, and starts the API again. The API applies any newer
   migrations as it starts. If a step fails, the API stays stopped: fix the cause and run the restore again.

This **replaces every company's data** with the backup, so everything entered since then is lost. To recover a single
record, restore onto a spare server and copy what's needed instead.

On a new server: restore Tycount first (its guide, section 8), then do the first deploy with the **same** `.env.prod`,
then restore TyHR.

## Updating

```sh
cd /opt/tyhr
BACKUP_PASSPHRASE_FILE=/root/tyhr-backup.pass deploy/backup.sh
git pull
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build
curl https://<TYHR_DOMAIN>/api/health
```

If `deploy/caddy/sites/` changed, also reload Caddy:
`cd /opt/tycount/Tycount && docker compose exec caddy caddy reload --config /etc/caddy/Caddyfile`.

Migrations run automatically when the API starts. If an update goes wrong:
1. Check out the previous version and run `up -d --build` again.
2. If the new version had already changed the database, the old version may not run on it. Restore the backup taken
   before the update.

## Monitoring

- **Uptime:** watch `https://<TYHR_DOMAIN>/api/health` from outside, e.g. UptimeRobot every 5 minutes. It returns 200
  only when the API and the database both answer.
- **Logs:** `docker compose -f docker-compose.prod.yml --env-file .env.prod logs -f tyhr-api`. Caddy's and
  Postgres's are in Tycount's stack.
  - Warnings worth acting on: emails that failed, and "database is not reachable".
- **Disk:** `df -h` and `docker system df`. Prune old images after updates with `docker image prune`.
- **Backups:** check `backups/backup.log` daily, and alert if no new `tyhr-db-*` file has appeared in 26 hours.

## Security checklist

- **Exposure:** TyHR publishes no ports. The API trusts `X-Forwarded-For`, which is only safe while Tycount's Caddy is
  the one thing in front of it.
- **Database:** the `tyhr` login isn't a superuser, and only it can connect to `tyhr`. Tycount's databases are closed to
  it (`REVOKE CONNECT`, first deploy step 4).
- **Settings file:** `.env.prod` is `chmod 600`, owned by root, and not in git.
- **Secrets key:** `TYHR_SECRETS_KEY` and the backup passphrase are stored apart from the backups.
- **Keycloak:** direct access grants are off on `tyhr-web`, and only Tycount's operator company is set as
  `PLATFORM_TENANT`.
- **SMTP:** the mail account is used only by TyHR, with SPF/DKIM set up for `MAIL_FROM`'s domain.
- **Updates:** OS security updates are on (e.g. `unattended-upgrades`). Rebuild with `--pull` monthly to pick up base
  image fixes.
- **Company isolation:** this is enforced in the database layer and tested end to end (`TenantIsolationTests`). Run
  the full test suite before deploying a change.

## Load test

```sh
cd api && TYHR_LOAD=5000 dotnet test --filter LoadTests --logger "console;verbosity=detailed"
```

It builds a company of `TYHR_LOAD` employees and times payroll, outputs, reports and exports. It needs Docker. On a
laptop, the times for 5,000 employees were:

| Step | Time |
|---|---|
| Calculate the payroll | about 1 s |
| Payroll outputs (bank file, journal) | about 0.1 s each |
| Statutory returns | about 1.3 s |
| Reports and Excel exports | under 0.5 s each |
| Rendering and emailing every payslip | about 22 s of work, sent 100 per request; the page keeps sending until done |

At 20,000 employees, the payroll calculates in about 3 seconds.
