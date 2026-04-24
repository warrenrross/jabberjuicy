# JabberJuicy

> *'Twas brillig, and the slithy toves did gyre and gimble in the wabe...*

A full-stack juice bar ordering application built for **Echo Team** — DASC Data Management, Spring 2026.

---

## What Is This?

JabberJuicy is a fictional juice bar chain inspired by Lewis Carroll's *Jabberwocky*. This app lets customers browse the menu, build an order, check out, and review their order history — all backed by a live SQL Server database.

The name, the menu items, the store addresses, and half the copy on the pages are pulled straight from the poem. If you order a *Frabjous Signature* at *123 Tulgey Lane*, you're in the right place.

---

## Team — Echo

| Name | Role |
|---|---|
| Warren Ross | App development, deployment |
| Adrian Munoz | App development |
| Grayson Bandy | Database design, SQL queries, user's manual |
| Andrew Cox | Database design, SQL queries |

---

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core Minimal API, .NET 10 |
| Database | SQL Server (external host) |
| Data access | Raw `SqlCommand` / `SqlDataReader` — no ORM |
| UI | Bootstrap 5.3 via CDN, inline HTML (no Razor) |
| Auth | Server-side sessions + PBKDF2-SHA256 password hashing |
| Hosting | Railway |

---

## Running Locally

> Quick reference: [`LOCAL_DEV.md`](LOCAL_DEV.md)

**Prerequisites:** .NET 10 SDK

```bash
git clone <repo-url>
cd WebAppWithDB_Starter_v2/WebAppWithDB_Starter_v2
dotnet run
```

The app starts at `http://localhost:5005`.

**Why `appsettings.Development.json` is required:**
The file `appsettings.json` (committed to this repo) intentionally contains an empty connection string — credentials are never stored in git. When running locally, .NET automatically loads `appsettings.Development.json` on top of it, which is where your real database credentials live. This file is listed in `.gitignore` and will never be committed.

Create the file at `WebAppWithDB_Starter_v2/WebAppWithDB_Starter_v2/appsettings.Development.json` with this template — fill in your own values:

```json
{
  "ConnectionStrings": {
    "JabberJuicy": "Data Source=YOUR_SERVER;Initial Catalog=YOUR_DATABASE;User Id=YOUR_USERNAME;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

Without this file the app will start but all database calls will fail.

---

## Deploying to Railway

1. Push this repo to GitHub
2. New project on [railway.app](https://railway.app) → Deploy from GitHub
3. **Set the Root Directory** — Railway's build tool scans the repo root and will fail to detect .NET if you skip this step. Go to your service → **Settings** → **Source** → **Root Directory** and set it to:
   ```
   WebAppWithDB_Starter_v2/WebAppWithDB_Starter_v2
   ```
4. **Set the environment variable** — Railway cannot read `appsettings.Development.json` (it's gitignored). Go to your service → **Variables** tab and add:
   ```
   Name:  ConnectionStrings__JabberJuicy
   Value: Data Source=YOUR_SERVER;Initial Catalog=YOUR_DB;User Id=YOUR_USER;Password=YOUR_PASS;TrustServerCertificate=True;
   ```
   The double underscore `__` is how .NET maps environment variables to nested config keys. Without this the app will start but all database calls will fail.
5. Add custom domain under **Settings** → **Networking** → **Generate Domain** (for a free `*.up.railway.app` URL) or **Custom Domain** to use your own.

**Pointing a Hostinger domain at Railway:**
- In Hostinger hPanel → Domains → DNS Records, add:
  - CNAME `www` → `your-app.up.railway.app`
  - CNAME `@` → `your-app.up.railway.app`
- Add both `yourdomain.com` and `www.yourdomain.com` under Railway → Settings → Networking → Custom Domain
- DNS propagation takes 5–30 minutes. If the apex (`@`) CNAME doesn't resolve, try replacing it with an A record or use Cloudflare as your DNS provider (supports CNAME flattening on apex domains).

---

## Database Schema

See [`Support_Material/JabberJuicy_Master_Schema.txt`](Support_Material/JabberJuicy_Master_Schema.txt) for the full `CREATE TABLE` statements and seed data. Incremental changes to the live DB are tracked in [`Support_Material/db_changes.txt`](Support_Material/db_changes.txt). Admin schema additions (5 new tables) are in [`admin_db_sql.txt`](admin_db_sql.txt) — run this after the core schema.

**Core tables:** `Customer`, `Item`, `Location`, `PaymentType`, `[Order]`, `OrderItem`, `JabberWonkTransaction`

**Admin tables:** `AdminUser`, `AdminLocationAccess`, `SupportCase`, `SupportCaseNote`, `AdminAuditLog`

All primary keys are `IDENTITY(1,1)` — do not specify ID values on `INSERT`.

Order status values: `Pending` → `Completed` (pickup confirmed) or `Cancelled`.

---

## App Pages

| Route | Auth | Description |
|---|---|---|
| `/` | — | Landing / hero page |
| `/register` | — | Create account |
| `/login` | — | Sign in |
| `/home` | ✓ | Dashboard with points balance |
| `/menu` | — | Public item catalog |
| `/order` | ✓ | Build cart |
| `/checkout` | ✓ | Location, payment, confirm |
| `/receipt` | ✓ | Order confirmation |
| `/history` | ✓ | Past orders |
| `/account` | ✓ | Change username |
| `/account/success` | ✓ | Username change confirmation |
| `/pickup` | ✓ | Select pending order to confirm |
| `/pickup/{id}` | ✓ | Confirm or cancel a specific order |
| `/pickup/{id}/success` | ✓ | Pickup confirmed + drink quote |
| `/admin/login` | Admin | Admin login (separate from customer auth) |
| `/admin` | Admin | Admin dashboard — 8 operational + analytics widgets |
| `/admin/points-adjust` | Admin | POST — manual points adjustment with audit log |
| `/admin/logout` | Admin | Clear admin session |

---

## The Poem

*Jabberwocky* by Lewis Carroll (1871)

```
'Twas brillig, and the slithy toves
  Did gyre and gimble in the wabe;
All mimsy were the borogoves,
  And the mome raths outgrabe.

"Beware the Jabberwock, my son!
  The jaws that bite, the claws that catch!
Beware the Jubjub bird, and shun
  The frumious Bandersnatch!"

He took his vorpal sword in hand;
  Long time the manxome foe he sought—
So rested he by the Tumtum tree,
  And stood awhile in thought.

And, as in uffish thought he stood,
  The Jabberwock, with eyes of flame,
Came whiffling through the tulgey wood,
  And burbled as it came!

One, two! One, two! And through and through
  The vorpal blade went snicker-snack!
He left it dead, and with its head
  He went galumphing back.

"And hast thou slain the Jabberwock?
  Come to my arms, my beamish boy!
O frabjous day! Callooh! Callay!"
  He chortled in his joy.

'Twas brillig, and the slithy toves
  Did gyre and gimble in the wabe;
All mimsy were the borogoves,
  And the mome raths outgrabe.
```
