# GpsSync backend (SQL Server) — auth + profiles slice

Replacement backend for the Supabase-based HitachiDispatch app. ASP.NET Core (**.NET 10**) Web API
+ **SQL Server** (EF Core), JWT auth with rotating refresh tokens. This first slice covers
**auth + profiles** (enough to make login / create-user / splash / settings work).

## Prerequisites
- .NET 10 SDK
- Docker (for local SQL Server)

## Secrets (do NOT commit real values)
`appsettings.json` ships with `__SET_VIA_ENV__` placeholders for the DB password and JWT key.
Provide real values at runtime via environment variables (see `.env.example`):
- `ConnectionStrings__Default` — full SQL Server connection string (incl. the SA password)
- `Jwt__Key` — a long random secret (≥ 32 chars) used to sign JWTs

Copy `server/.env.example` to `server/.env` (git-ignored) and fill it in.

## 1. Start SQL Server (local dev)
```bash
export SA_PASSWORD='choose-a-strong-password'   # 8+ chars, upper/lower/digit/symbol
docker run -d --name geo-sqlserver \
  -e "ACCEPT_EULA=Y" \
  -e "MSSQL_SA_PASSWORD=$SA_PASSWORD" \
  -e "MSSQL_PID=Developer" \
  -p 1433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest
```

## 2. Run the API
```bash
cd server/GpsSync.Api
set -a; source ../.env; set +a          # load ConnectionStrings__Default and Jwt__Key
dotnet run
```
- Listens on `http://0.0.0.0:5080` (see `Urls` in appsettings).
- On startup it **applies EF migrations** (creates the `GpsSync` DB + tables) and **seeds an admin**.
- OpenAPI contract: `http://localhost:5080/openapi/v1.json` · health: `/health`.

### Seeded test admin
`admin@local.test` / `Admin!23`  (is_admin = true)

## 3. Endpoints (this slice)
| Method | Route | Replaces (Supabase) |
|---|---|---|
| POST | `/auth/register` | `Auth.SignUp` + profile insert |
| POST | `/auth/login` | `Auth.SignIn` |
| POST | `/auth/refresh` | `Auth.RefreshSession` |
| POST | `/auth/logout` | `Auth.SignOut` |
| GET  | `/auth/me` | `Auth.CurrentUser` |
| PATCH| `/auth/user` | `Auth.Update(display_name)` |
| GET  | `/profiles/{userId}` | `From<ProfileRecord>().Single()` |
| PATCH| `/profiles/{userId}` | `From<ProfileRecord>().Set(...).Update()` |

Auth = JWT bearer. `/profiles` and the `[Authorize]` auth routes require the access token.
Authorization rule: a user may act on their **own** profile; **admins** may act on any.

## Emulator networking (important)
The Android emulator cannot reach `localhost` (that's the emulator itself). When the API runs on the
**same machine** as the emulator, the app must use **`http://10.0.2.2:5080`**. A physical device must
use the PC's LAN IP. Cleartext HTTP must be allowed in the Android app (see the app's
network-security config).

## EF migrations
```bash
cd server/GpsSync.Api
dotnet ef migrations add <Name> -o Data/Migrations
dotnet ef database update   # (also applied automatically on app startup)
```

## Not yet migrated (next slices)
`dispatch_jobs` and `gps_pings` tables exist in the schema, but their controllers + the app-side
rewrite (Jobs, AdminJobs, EngineerStatus, Map, GPS services) come later. Those app screens still
reference Supabase until then.
