# ServiceHub BD — API server

ASP.NET Core Web API (.NET 10) + PostgreSQL (EF Core / Npgsql). The data model follows
[`docs/database/ERD.md`](../docs/database/ERD.md).

## Run locally

Requirements: .NET 10 SDK and PostgreSQL 15+. The database needs the `ltree` and `btree_gist` extensions; both ship with PostgreSQL contrib.

```bash
cd server
dotnet tool restore                      # dotnet-ef
createdb servicehub_dev                  # or point ConnectionStrings:Default elsewhere
dotnet run --project src/ServiceHub.Api  # http://localhost:5291
```

In `Development` the app applies migrations and seeds on startup. It seeds roles and permissions, categories, sample Bangladesh zones and a bootstrap super admin (`01700000000` / `Admin@12345`).

- API reference (Scalar): <http://localhost:5291/scalar>
- OpenAPI document: <http://localhost:5291/openapi/v1.json>
- Health check: <http://localhost:5291/health>
- Sample requests: [`ServiceHub.Api.http`](src/ServiceHub.Api/ServiceHub.Api.http)

`appsettings.Development.json` connects over the local Unix socket as your OS user. For production, set these through environment variables or user-secrets, never committed files:

| Setting | Purpose |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL connection |
| `Jwt__SigningKey` | ≥ 32 bytes, random |
| `Database__MigrateOnStartup` | `true` to migrate + seed on boot |
| `Seed__SuperAdminPhone` / `Seed__SuperAdminPassword` | first admin, only used if that phone has no account; clear after bootstrap |
| `Cors__Origins__0` … | allowed front-end origins |

## Tests

```bash
dotnet test
```

The integration tests boot the real API against a throwaway PostgreSQL database. Each run creates one and drops it afterwards. By default they connect via `/var/run/postgresql` as the current OS user. Override with `SERVICEHUB_TEST_PG`, for example `Host=localhost;Username=postgres;Password=postgres`.

## Migrations

```bash
dotnet ef migrations add <Name> -p src/ServiceHub.Api -o Data/Migrations
dotnet ef database update -p src/ServiceHub.Api
```

Roles and permissions are **not** migrations. They live in `Access/Catalog.cs` and are synced into the database on every startup.

## Layout

```
src/ServiceHub.Api/
  Domain/            entities + enums (stored as snake_case strings, e.g. "info_requested")
  Data/              AppDbContext (snake_case tables, ltree, jsonb), migrations, DataSeeder
  Access/            permission & role catalogue, AccessService (business / platform / zone scope)
  Common/            errors → problem details, current user, audit logger, notifier, BD time, phone utils
  Features/<Area>/   controllers + DTOs per module
tests/ServiceHub.Api.Tests/   end-to-end flows against PostgreSQL
```

## Access model

- One account per person. Every account is a customer.
- **Business roles:** a user can hold a different role on each business page, through `business_members` (owner, manager, staff, page moderator, analyst). Non-members get **404**, so private workspaces don't reveal that they exist.
- **Platform and zone roles:** held through `platform_role_assignments` (super admin, admin, system moderator, inspector, marketer, support agent). An assignment can be narrowed to a `geo_zones` subtree and/or a category. A Gazipur inspector covers every upazila and union in Gazipur.
- **Conflict of interest:** moderators and inspectors can't act on a business they are a member of.
- **Audit:** every privileged action goes to `audit_logs`. Moderator decisions also go to `moderation_actions`, and each one requires a reason.

## API overview

| Area | Endpoints |
|---|---|
| Auth | `POST /api/auth/register · login · refresh · logout · otp · verify-phone · password/reset` |
| Me | `GET/PATCH /api/me`, `GET /api/me/workspaces`, `PUT /api/me/workspace`, notifications, favourites, invitations, bookings |
| Taxonomy | `GET /api/zones`, `GET /api/categories`, `PUT /api/admin/categories` |
| Discovery (public) | `GET /api/public/businesses` (search: q, category, zone subtree, filters, sort), `/{slug}`, `/{id}/reviews`, `/{id}/availability` |
| Business workspace | `POST /api/businesses`, `GET/PATCH /{id}`, `PUT /{id}/hours`, `/booking-settings`, closures, `POST /{id}/verification` |
| Services | `/api/businesses/{id}/services` CRUD + duplicate, dynamic booking fields |
| Bookings | `POST /api/bookings` (idempotent, slot-locked); business: list, summary, confirm/reject/cancel/complete/no-show, assignee, note |
| CRM | `/api/businesses/{id}/customers` list (segments), detail, VIP/tags, private notes |
| Reviews & reports | review after completed booking, business reply, report review/business/user |
| Team | `/api/roles`, members, invitations (by phone), accept, role change (rank rules), remove |
| Moderation | verification queue (zone/category scoped), start/approve/reject/request-info/request-inspection, reports, suspend/reinstate, history |
| Inspections | zone task list, claim, start (GPS), submit findings |
| Admin | stats, users (suspend/activate), role assignments (grant/revoke), audit logs |

## Not built yet

These tables are in the ERD but have no endpoints yet: file uploads (gallery, service images, review photos, inspection photos), promotions and marketing campaigns, featured listings and ads, announcements, analytics rollups (`business_daily_stats`), reschedule requests, booking reminders (needs a background scheduler), and SMS/email gateways (`LoggingSmsSender` only writes to the log).
