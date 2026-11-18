# ServiceHub BD — Web client

React 19 + TypeScript + Vite, React Router, TanStack Query and Tailwind CSS v4. The colour, radius and shadow tokens live in `src/index.css`. All UI text is Bangla.

## Run

```bash
# 1. API (see ../server/README.md) on http://localhost:5291
cd server && dotnet run --project src/ServiceHub.Api

# 2. Client on http://localhost:5173 (proxies /api to the API, so no CORS setup is needed)
cd client && npm install && npm run dev
```

To point the proxy at a different API, use `API_URL=http://host:port npm run dev`.

Dev super admin login: `01700000000` / `Admin@12345`.

## Scripts

| Command | |
|---|---|
| `npm run dev` | dev server with HMR |
| `npm run build` | type-check + production build to `dist/` |
| `npm run typecheck` | TypeScript only |

## Structure

```
src/
  api/client.ts        fetch wrapper: bearer token, one shared auto-refresh, problem-details → ApiError (Bangla message)
  api/types.ts         DTOs mirroring the server (enums are snake_case strings)
  auth/AuthContext.tsx session restore, login/register/logout, workspace list
  components/          Layouts (public, customer, business, platform), WorkspaceSwitcher, ui primitives, shared pickers/cards
  lib/format.ts        Bangla numerals, Asia/Dhaka dates, taka, status labels
  pages/public/        landing, login/register, search, business profile, booking wizard
  pages/me/            my bookings (cancel, review), notifications, profile (+ phone OTP, team invitations)
  pages/business/      create-business wizard, dashboard, appointments, services, CRM, reviews, team, settings/verification
  pages/platform/      moderation (queue, review, reports, history), inspections, admin (stats, users, roles, audit)
```

## Routes

| Path | Workspace |
|---|---|
| `/`, `/search`, `/b/:slug` | public |
| `/b/:slug/book` | booking wizard (login required) |
| `/me/bookings · notifications · profile` | customer |
| `/business/new` | create a business page |
| `/w/:businessId/…` | business workspace (menu items follow the member's permissions) |
| `/moderation`, `/inspections`, `/admin` | platform workspaces (shown in the switcher when the user holds the role) |

## Notes

- The refresh token is kept in `localStorage` and the access token only in memory. For production, consider moving refresh to an httpOnly cookie, which needs a small server change.
- Uploads aren't built yet. Logos, covers and verification documents take URLs for now.
