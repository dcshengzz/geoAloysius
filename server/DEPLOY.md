# Deploying GpsSync to your server (Docker Compose + HTTPS)

This stands up the whole backend with **one command**. Three containers:

| Container | Role | Exposed to internet? |
|---|---|---|
| `caddy`   | Reverse proxy, **automatic HTTPS** (Let's Encrypt) | **Yes** — ports 80 + 443 |
| `api`     | The ASP.NET Core API | No (internal only) |
| `db`      | SQL Server 2022, data in a persistent volume | No (internal only) |

Only Caddy faces the internet. SQL Server is **never** publicly reachable.

---

## 0. Prerequisites (on the server)

- **Linux, x86_64** (SQL Server's image is amd64-only), **≥ 4 GB RAM** (SQL Server needs ~2 GB).
- **Docker Engine + Compose plugin**: `docker --version` and `docker compose version` both work.
  Install: <https://docs.docker.com/engine/install/>
- A **public IP**, and **inbound TCP 80 and 443 open** (cloud firewall / security group + any OS firewall).
  Port 80 is required for the Let's Encrypt certificate challenge, not just 443.
- One of:
  - a **domain** you can point at the server (e.g. `api.yourcompany.com`), **or**
  - **nothing** — use the free `sslip.io` trick (see step 2), which needs only the public IP.

---

## 1. Get the code onto the server

```bash
git clone <your-repo-url> geo
cd geo
git checkout migrate/sqlserver-auth-profiles   # the branch with the backend + deploy files
cd server
```

## 2. Configure secrets + address

```bash
cp .env.example .env
nano .env        # (or vi) fill in the three values
```

Set:

- **`SA_PASSWORD`** — SQL Server `sa` password. 8+ chars, must have upper, lower, digit **and** a symbol.
- **`JWT_KEY`** — a long random secret. Generate one: `openssl rand -base64 48`
- **`SITE_ADDRESS`** — the public HTTPS address:
  - **With a domain:** `api.yourcompany.com`, and create a **DNS A record** for it pointing at the server's public IP.
  - **No domain (free):** `<PUBLIC-IP-with-dashes>.sslip.io` — e.g. if the IP is `34.87.120.9`, use
    `34-87-120-9.sslip.io`. `sslip.io` resolves that name to the IP automatically; no DNS setup.

> If you use a domain, make sure DNS actually resolves **before** step 3 (`dig +short SITE_ADDRESS`
> should return the server IP), otherwise Caddy can't get a certificate yet.

## 3. Launch

```bash
docker compose up -d --build
```

First run: pulls images, builds the API, starts SQL Server (waits until it's healthy), applies EF
migrations, seeds the admin, then Caddy fetches the HTTPS certificate (a few seconds once DNS + ports
are correct).

## 4. Verify

```bash
# from anywhere:
curl https://<SITE_ADDRESS>/health          # -> {"status":"ok"}   (valid HTTPS, no -k needed)

# on the server, watch it come up / debug:
docker compose ps
docker compose logs -f api      # look for "Applying migration" then "Now listening on"
docker compose logs -f caddy    # look for "certificate obtained successfully"
```

Seeded admin for the first login: **`admin@local.test` / `Admin!23`** — change/remove this before real use (see Ops).

---

## 5. Build the APK that points at the cloud

The app URL switches by build type (`AppConfig.cs`): **Debug** → local, **Release** → cloud.

1. Edit `AppConfig.cs` (repo root), in the `#else` (Release) branch, set:
   ```csharp
   public const string ApiBaseUrl = "https://<SITE_ADDRESS>";   // e.g. https://api.yourcompany.com
   ```
2. In Visual Studio (Windows), build the app in **Release** for Android → produces the `.apk`
   (`Build > Archive`, or a Release build to a device).
3. Distribute that `.apk`. Engineers **install it, open it, log in** — over any mobile data / Wi-Fi.
   No VPN, no per-phone setup, and your server doesn't need to be on the same network.

> Cleartext (HTTP) is only permitted to the local dev IPs in
> `Platforms/Android/Resources/xml/network_security_config.xml`; the cloud address uses HTTPS and
> needs no exception. Nothing to change there.

---

## 6. Day-to-day: shipping a change

- **API / backend change** → on the server: `git pull` then `docker compose up -d --build`
  (rebuilds only the API; DB + certs untouched). ~1–2 min, no data loss.
- **Database schema change** → add an EF migration in dev
  (`dotnet ef migrations add <Name> -o Data/Migrations`), commit, then the redeploy above applies it
  automatically on startup.
- **App (screen/behavior) change** → rebuild + redistribute the APK (step 5). This is the only change
  that requires engineers to reinstall — so prefer doing work in the API where you can.

**Local inner loop (fast, before deploying):** run SQL Server + the API on your machine and the app
from Visual Studio in **Debug** (it hits `http://10.0.2.2:5080` automatically). You can also point a
Debug build at the live cloud URL to test against production.

---

## 7. Ops / hardening (before real production traffic)

- **Change the seeded admin**: `Admin!23` is hardcoded in `GpsSync.Api/Data/SeedData.cs`. Change it
  (or disable the seed) and rebuild, or log in once and reset it.
- **Backups**: schedule a dump of the `GpsSync` database and copy it off the server. The DB lives in
  the `mssql_data` Docker volume — the volume survives redeploys, but back it up anyway.
  Example dump: `docker compose exec db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C -Q "BACKUP DATABASE GpsSync TO DISK='/var/opt/mssql/backup.bak'"`
- **Secrets**: `server/.env` is git-ignored — keep it that way. Rotate `JWT_KEY`/`SA_PASSWORD` if ever exposed.
- **Email**: password-reset codes go to the API log by default. For real emails, set the `Email__*`
  vars in `docker-compose.yml` (SMTP) and redeploy.

## 8. Troubleshooting

- **`curl` fails / cert error** → Caddy hasn't issued the cert. Check: DNS resolves to this server
  (`dig +short <SITE_ADDRESS>`), and ports **80 and 443** are open in the cloud firewall. See
  `docker compose logs caddy`.
- **API keeps restarting** → `docker compose logs api`. Usually the DB isn't ready (it waits via a
  healthcheck) or the connection string password mismatch — confirm `SA_PASSWORD` in `.env`.
- **SQL Server won't start** → almost always < 2 GB RAM available, or an ARM host (needs x86_64).
- **Stop / start / reset**:
  ```bash
  docker compose down          # stop (keeps data)
  docker compose down -v       # stop AND delete the database volume (wipes all data)
  docker compose up -d         # start again
  ```
