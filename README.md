# TyHR

Multi-tenant HR and payroll for East African companies, one of the Tysoft products. Each company is a tenant with a
country pack (Uganda first, then Kenya) that sets its currency, ID and tax-number formats, phone prefix and
statutory payroll rules.

**What it does:**
- **Core HR:** company onboarding, org structure and chart, employee records (ID, bank and mobile-money numbers
  encrypted), contracts and salary history, documents with expiry reminders, Excel import and export, the staff
  directory and an audit trail.
- **Leave and attendance:** balances and approvals, public holidays, schedules, clock-ins and device imports.
- **Payroll:** Uganda and Kenya statutory rules, approval steps, payslips, bank and mobile-money files, statutory
  returns and the accounting journal.
- **Self-service:** profile changes, payslips, leave and an approvals inbox.
- **Recruitment and onboarding:** requisitions, a public careers page, interviews, offers, hiring, checklists and
  probation.
- **Performance:** review cycles, goals, 360° feedback, calibration, improvement plans and salary reviews.
- **Exits:** resignations, clearance, final dues, certificate of service and retention.
- **Reports:** headcount, turnover, leave liability and payroll cost, with Excel export.

## Stack

- **API:** .NET 10, ASP.NET Core, EF Core 10 on PostgreSQL 17, MailKit and ClosedXML. The code is in four layers: `Domain`, `Application`, `Infrastructure` and `Api`.
- **Web:** React 19, TypeScript, Vite 8, Mantine 9, TanStack Query and react-router.
- **Sign-in:** Tycount's Keycloak, in the shared `tysoft-apps` realm, so one login works across Tysoft products. TyHR has its own clients (`tyhr-web`, `tyhr-api`, `tyhr-provisioner`) and roles. A user's company comes from their `tenant` attribute, the same one Tycount uses.
- **Tests:** xUnit with Testcontainers (a real Postgres in Docker) for the API, and vitest for the web.

## Running it locally

You need:
- the .NET 10 SDK;
- Node 22.12 or later (`nvm install` reads `web/.nvmrc`);
- Docker;
- Tycount's repository next to this one (`../erp/Tycount`), for Keycloak;
- the shared Tysoft Postgres 17 container running (`postgres_container`; set `PG_CONTAINER` if yours is named differently).

```sh
# 1. Keycloak (Tycount's), on http://localhost:8082
(cd ../erp/Tycount && docker compose up -d keycloak)

# 2. TyHR's clients, roles and dev users in the tysoft-apps realm (safe to re-run).
#    Saves the provisioner secret to the API's user-secrets and the users' passwords to deploy/keycloak/dev-users.local.txt.
deploy/keycloak/setup.sh --dev-users

# 3. TyHR's database on the shared Postgres (container postgres_container, port 5432), with its connection string
#    saved to the API's user-secrets; and Mailpit (http://localhost:8025)
deploy/postgres/setup-dev.sh
docker compose up -d

# 4. API on http://localhost:5262 (applies migrations and seeds the demo company on start)
(cd api && dotnet run --project src/TyHr.Api --launch-profile http)

# 5. Web app on http://localhost:5174
(cd web && npm install && npm run dev)
```

Sign in at http://localhost:5174 with one of the dev users in `deploy/keycloak/dev-users.local.txt`. Their passwords are generated, and the file isn't committed.

| User | Role | Sees |
|---|---|---|
| `hradmin1` | HR admin | everything in the demo company, including pay and company settings |
| `hrofficer1` | HR officer | records, personal details and documents, but no pay |
| `payroll1` | Payroll officer | records and pay; documents read-only |
| `manager1` | Line manager | their team: everyone below the employee whose work email is their sign-in email |
| `employee1` | Employee | the directory and org chart |
| `platform1` | Platform admin | all companies (Platform → Companies) |

Ports are offset from sacco and Tycount so all three stacks can run together:

| Service | Port |
|---|---|
| API | 5262 |
| Web | 5174 |
| Postgres (shared) | 5432 |
| Mailpit SMTP | 1025 |
| Mailpit UI | 8025 |
| Keycloak (Tycount) | 8082 |

Uploaded documents go to `data/files/`, which isn't committed. TyHR's own emails, such as expiry reminders, land in Mailpit. Keycloak's emails, such as invitations, go through the tysoft-apps realm's SMTP settings. Until those are configured, inviting someone who has no account fails cleanly, and nothing is created.

## Tests and checks

```sh
(cd api && dotnet test)                                  # needs Docker running
(cd web && npm run lint && npm test && npm run build)
```

CI (`.github/workflows/ci.yml`) runs these checks and builds the production images.

## Production

TyHR runs on Tycount's production server. `docker-compose.prod.yml` is just the API and the web app; Caddy,
Keycloak and Postgres are Tycount's, connected by `deploy/tycount.override.yml`.

[docs/deploy.md](docs/deploy.md) is the guide: first deploy and settings, backups (`deploy/backup.sh`) and restores
(`deploy/restore.sh`), updates, monitoring, the security checklist and load-test results.
