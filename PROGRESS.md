# JabberJuicy — Project Progress & Agent Memory

## Completed

### Phase 1 — Design & Documentation (Mar 31 – Apr 6) ✅
- Project proposal (`Echo_Team_Proposal.docx`)
- Conceptual ER model (`Concept_drawing.drawio`, `Conceptual Design.drawio.png`)
- Full ERD with `OrderItem` bridge table
- Data dictionary (`Echo_Data_Dictionary.xlsx`) with PK/FK relationship sheet
- Business rules (`BusinessRules.docx`)
- Project timeline (`Echo_Group_Timeline.xlsx`)
- UI mockups/wireframes (`User_Interface.pptx`, `Non-Functional_Prototype.pptx`)

### Phase 2 — Non-Functional Prototype (Apr 7 – Apr 20, in progress)
- ASP.NET Core minimal API web app (`WebAppWithDB_Starter_v2/`) — Warren & Adrian
  - Routes: `/`, `/login`, `/register`, `/logout`, `/home`, `/menu`, `/order`, `/checkout`, `/receipt`, `/history`, `/error`
  - Session-based cart (JSON in server-side session)
  - PBKDF2/SHA-256 password hashing
  - Bootstrap 5.3 UI, all HTML inline (no Razor, no static files)
  - Connects to external SQL Server: `sql.isys.algoarmada.com` / `DASC_1_Spring2026_Shipp_TeamEcho`
- Credentials moved from hardcoded constants → `IConfiguration` / `appsettings` system
- `.gitignore` created (excludes `bin/`, `obj/`, `.vs/`, `appsettings.Development.json`)
- App verified running locally at `http://localhost:5005`

---

## Current Focus

**Deployment to Railway + custom Hostinger domain** — app is working locally; next step is getting it live at the jabberjuicy domain before the Apr 28–30 presentation.

---

## Roadmap

1. **Fix credentials before public GitHub push** — `appsettings.json` currently contains the real DB password. Revert it to an empty placeholder; put credentials in `appsettings.Development.json` (gitignored) for local dev and in Railway env vars for production. (See Known Issues #1.)
2. **Push repo to GitHub** — initialize git, commit everything except gitignored files, push to a new repo (can be public once step 1 is done).
3. **Deploy to Railway** — new project from GitHub repo; Railway auto-detects .NET 10. Set env var `ConnectionStrings__JabberJuicy` to the full connection string.
4. **Add custom domain on Railway** — Settings → Domains → add `jabberjuicy.com`; Railway returns a CNAME value.
5. **Point Hostinger DNS to Railway** — In Hostinger DNS panel, add CNAME record: `www` → Railway CNAME value. Add redirect or ALIAS for the apex domain.
6. **Phase 3 tasks (Apr 21–27)** — SQL queries (Grayson & Andrew), functional DB & app integration (Adrian & Warren), user's manual (Grayson).

---

## Known Issues / Lessons Learned

### #1 — `appsettings.json` vs `appsettings.Development.json` for credentials
**What happened:** After setting up the config system, the real DB connection string was placed directly in `appsettings.json` (which IS committed to git) rather than only in `appsettings.Development.json` (which is gitignored).

**Rule:** `appsettings.json` = placeholder/empty string, safe to be public. `appsettings.Development.json` = real credentials, gitignored. Railway env var `ConnectionStrings__JabberJuicy` = real credentials for production.

**Before pushing to GitHub:** confirm `appsettings.json` has `"JabberJuicy": ""` (empty).

### #2 — Port 5005 stays occupied between runs
**What happened:** `dotnet run` fails with "address already in use" if a previous instance wasn't cleanly stopped.

**Fix:** `lsof -ti :5005 | xargs kill -9` before restarting.

### #3 — .NET 10 target framework
The project targets `net10.0`. Railway and Azure App Service both support it. Hostinger **shared hosting does not run .NET** — a compute platform (Railway, Azure, Render, Fly.io) is required.

### #5 — LocationID (and likely other PKs) are IDENTITY columns, not CHAR(8)
**What happened:** Data dictionary documents `LocationID` as `CHAR(8)` PK, but the live database implements it as an auto-increment IDENTITY integer. Inserting with an explicit `LocationID` value throws Msg 544.

**Rule:** Never specify PK values in INSERT statements for this database. Omit the ID column and let SQL Server assign it. Use `WHERE LOC_StoreName = ...` (or another non-PK column) in UPDATE/DELETE when the auto-generated ID is unknown. Check other tables (Customer, Item, Order, etc.) — they likely follow the same pattern.

### #4 — No Razor / no static `wwwroot`
All HTML is generated as inline C# strings. There are no `.cshtml` files and no `wwwroot` folder. Do not attempt to add Razor Pages or MVC without a significant refactor — the current pattern is intentional for simplicity.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core Minimal API, .NET 10 |
| Database | SQL Server (external host: `sql.isys.algoarmada.com`) |
| ORM | None — raw `SqlCommand` / `SqlDataReader` via `Microsoft.Data.SqlClient` |
| UI | Bootstrap 5.3 (CDN), inline HTML strings |
| Auth | Session cookie + PBKDF2-SHA256 password hash |
| Hosting (prod) | Railway (planned) |
| Domain | Hostinger — jabberjuicy domain reserved |

---

## Team

| Member | Phase 2 Role | Phase 3 Role |
|---|---|---|
| Warren Ross | Non-Functional Prototype (app) | Functional DB & App |
| Adrian Munoz | Non-Functional Prototype (app) | Functional DB & App |
| Grayson Bandy | Physical DB Design | SQL Queries + User's Manual |
| Andrew Cox | Physical DB Design | SQL Queries |
