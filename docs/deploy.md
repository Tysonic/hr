# Deploying TyHR

This guide covers running TyHR in production on one server with Docker Compose: the first deploy, updates, backups and
restores, monitoring, and a security checklist. The files involved are `docker-compose.prod.yml`, `deploy/` and
`.env.prod`. `.env.prod` is never committed.

## What runs where

| Service | What it is | Reachable from |
|---|---|---|
| `caddy` | The only public entrance. It serves HTTPS on 80/443 with one wildcard certificate (Cloudflare DNS challenge), sends `/api/*` to the API and everything else to the web app. | The internet |
| `web` | The React app as static files (its own small Caddy). | Caddy only |
| `api` | The .NET API. It applies database migrations when it starts. | Caddy only (no published port) |
| `db` | PostgreSQL 17, data in the `db` volume. | The API only |
| Keycloak | **Not part of this stack.** Sign-in is MyBiz's Keycloak (realm `MyBiz`), shared so one sign-in covers both apps. | `AUTH_URL` |

Volumes: `db` (the database), `files` (uploaded documents and CVs), `caddy-data` (certificates) and
`dataprotection-keys`. Back up `db` and `files` (see [Backups](#backups)). Caddy can re-issue certificates.

Every company uses `https://<APP_DOMAIN>`. Which company a user belongs to comes from their sign-in, not from the
address. Anything at `https://<something>.<APP_DOMAIN>` is redirected there, because Keycloak only accepts sign-in
addresses registered one by one.

## Server

- **OS and software:** Linux with Docker Engine 24 or later and the Compose plugin, plus `git`, `openssl`
  and `jq`.
- **Size:** 2 vCPUs and 4 GB of RAM are plenty for a few thousand employees across all companies. The load test (below)
  calculates a 5,000-person payroll in about a second. Allow 20 GB of disk, plus room for backups.
- **Firewall:** open 22 (SSH), 80 and 443 only. Don't publish the API's or the database's port. The API trusts the
  `X-Forwarded-For` header, which is only safe while Caddy is the only thing that can reach it.

## First deploy

1. **DNS.** Point `APP_DOMAIN` and `*.APP_DOMAIN` (A records) at the server.
   - Leave them **DNS only** (grey cloud) in Cloudflare.
   - If you proxy them (orange cloud), Caddy sees Cloudflare's addresses instead of visitors' addresses. The careers
     page's per-visitor limits would then count many applicants as one visitor.
2. **Cloudflare token** for the certificate: an API token with *Zone → DNS → Edit* on the domain's zone.
3. **Keycloak.** On the server that runs the production Keycloak (the script uses its container), run:

   ```sh
   KEYCLOAK_CONTAINER=<keycloak container> KEYCLOAK_URL=https://auth.<domain> APP_DOMAIN=<APP_DOMAIN> deploy/keycloak/setup.sh
   ```

   - It adds TyHR's clients and roles to the MyBiz realm, lets `tyhr-web` sign in at `https://<APP_DOMAIN>`, and prints
     the provisioner secret.
   - It's safe to re-run, and keeps the addresses already registered. Don't change the realm by hand.
   - Check that **Direct access grants are off** on `tyhr-web`.
   - MyBiz's "own company only" sign-in steps check only the clients listed in their settings (`mybiz-web`). Don't add
     `tyhr-web` there: TyHR has one address for every company, so everyone would be refused.
4. **Settings.** Clone the repository (e.g. to `/opt/tyhr`) and create `.env.prod` with `chmod 600`:

   | Setting | Required | What it is |
   |---|---|---|
   | `APP_DOMAIN` | yes | e.g. `hr.tysoftltd.com` |
   | `ACME_EMAIL` | yes | Let's Encrypt account email |
   | `CLOUDFLARE_API_TOKEN` | yes | from step 2 |
   | `POSTGRES_PASSWORD` | yes | Postgres superuser password (`openssl rand -base64 24`) |
   | `TYHR_DB_PASSWORD` | yes | password of the `tyhr` database user (`openssl rand -base64 24`) |
   | `TYHR_SECRETS_KEY` | yes | encrypts ID, bank and mobile-money numbers: `openssl rand -base64 32`. **Losing it makes those fields unreadable.** Keep a copy somewhere safe, apart from the backups. |
   | `AUTH_URL` | yes | Keycloak's base URL, e.g. `https://auth.tycount.tysoftltd.com` |
   | `KC_PROVISIONER_SECRET` | yes | from step 3 |
   | `PLATFORM_TENANT` | no | MyBiz's operator company. Only its `PlatformAdmin`s become TyHR platform admins. |
   | `SMTP_HOST`, `SMTP_PORT`, `SMTP_SECURITY`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `MAIL_FROM` | no | outgoing mail (payslips, reminders, offers). Without `SMTP_HOST` nothing is sent, and reminders wait. `SMTP_SECURITY` is `StartTls` (587), `Ssl` (465) or `None`. |

   The database passwords only take effect when the `db` volume is first created. To change them later, change them in
   Postgres too.
5. **Start it:** `docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build`
6. **Check it:**
   - `curl https://<APP_DOMAIN>/api/health` should return `{"status":"ok"}`.
   - `docker compose -f docker-compose.prod.yml logs api` should show the migrations applied.
7. **First company.** Sign in as a platform admin, go to Platform → Companies, and add the company with its HR admin's
   email. The HR admin is invited by email to set a password, unless they already have a MyBiz sign-in.
8. **Backups.** Set them up now, before anyone enters data (next section).

## Backups

`deploy/backup.sh` saves the database (`pg_dump`, checked with `pg_restore --list` before it's kept) and the uploaded
files into `backups/`, both or neither. It removes backups older than `BACKUP_KEEP_DAYS` (default 14).

1. Make a passphrase file, keep a copy of it with `.env.prod`'s copy, and run the backup daily from cron:

   ```sh
   openssl rand -base64 32 > /root/tyhr-backup.pass && chmod 600 /root/tyhr-backup.pass
   # crontab -e
   15 2 * * * cd /opt/tyhr && mkdir -p backups && BACKUP_PASSPHRASE_FILE=/root/tyhr-backup.pass deploy/backup.sh >> backups/backup.log 2>&1
   ```

2. Copy backups off the server, because a backup on the same disk doesn't survive losing the server. For example, with
   rclone to any S3-compatible bucket, after the backup:

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
3. Stops the API, replaces the database and the uploaded files, and starts the API again. The API applies any newer
   migrations as it starts. If a step fails, the API stays stopped: fix the cause and run the restore again.

This **replaces every company's data** with the backup, so everything entered since then is lost. To recover a single
record, restore onto a spare server and copy what's needed instead.

On a new server: do steps 1–4 of the first deploy with the **same** `.env.prod`, then `up -d --build`, then restore.

## Updating

```sh
cd /opt/tyhr
BACKUP_PASSPHRASE_FILE=/root/tyhr-backup.pass deploy/backup.sh
git pull
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build
curl https://<APP_DOMAIN>/api/health
```

Migrations run automatically when the API starts. If an update goes wrong:
1. Check out the previous version and run `up -d --build` again.
2. If the new version had already changed the database, the old version may not run on it. Restore the backup taken
   before the update.

## Monitoring

- **Uptime:** watch `https://<APP_DOMAIN>/api/health` from outside, e.g. UptimeRobot every 5 minutes. It returns 200
  only when the API and the database both answer.
- **Logs:** `docker compose -f docker-compose.prod.yml logs -f api` (also `caddy` and `db`).
  - Warnings worth acting on: emails that failed, and "database is not reachable".
- **Disk:** `df -h` and `docker system df`. Prune old images after updates with `docker image prune`.
- **Backups:** check `backups/backup.log` daily, and alert if no new `tyhr-db-*` file has appeared in 26 hours.

## Security checklist

- **Exposure:** only 80/443 (and SSH) are open. `docker compose ps` shows no published port except Caddy's.
- **Settings file:** `.env.prod` is `chmod 600`, owned by root, and not in git.
- **Secrets key:** `TYHR_SECRETS_KEY` and the backup passphrase are stored apart from the backups.
- **Keycloak:** direct access grants are off on `tyhr-web`, and only MyBiz's operator company is set as
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
