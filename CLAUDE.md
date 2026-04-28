# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a **DASC Data Management course group project** for Echo Team. The deliverable is a relational database design and application for **JabberJuicy** — a fictional juice bar chain.

**Team members:** Grayson Bandy, Warren Ross, Adrian Munoz, Andrew Cox

## Working with Project Files

Most artifacts are binary/Office formats. To inspect them programmatically:

```bash
# Read Excel files (openpyxl is available)
python3 -c "import openpyxl; wb = openpyxl.load_workbook('Echo_Data_Dictionary.xlsx'); ..."
```

## Database Schema (JabberJuicy)

The conceptual model (`Concept_drawing.drawio`, `Conceptual Design.drawio.png`) centers on `Order` as the transaction entity, with four lookup/dimension tables feeding into it. The full ERD adds `OrderItem` as a bridge/line-item table.

**Note:** Live DB uses `INT IDENTITY(1,1)` PKs — the data dictionary still documents CHAR keys (known mismatch, documented below).

**Core tables and primary keys:**

| Table | PK | Notes |
|---|---|---|
| `Customer` | `CustomerID` INT | Name, email, phone, full address, `CUS_PointsBalance` (JabberWonk pts) |
| `Item` | `ItemID` INT | Name, description, category, unit price, stock qty |
| `Location` | `LocationID` INT | Store address, phone, manager |
| `PaymentType` | `PaymentTypeID` INT | Cash, Credit Card, Debit Card, **JabberWonk Points** |
| `Order` | `OrderID` INT | Date/time, total, status (`Pending`/`Completed`/`Cancelled`), notes; FK→Customer, Location, PaymentType |
| `OrderItem` | `OrderItemID` INT | OrderID+ItemID line items; `ORI_Subtotal` is computed |
| `JabberWonkTransaction` | `JabberWonkTransactionID` INT | Points earn/redeem audit log; FK→Customer, Order; `JWT_` prefix; `OrderID` is NOT NULL |

**Admin tables** (applied via `admin_db_sql.txt` — run after core schema):

| Table | PK | Notes |
|---|---|---|
| `AdminUser` | `AdminUserID` INT | Roles: Admin/Manager/Support; `ADM_IsGlobalAccess` BIT; `ADM_IsActive` BIT |
| `AdminLocationAccess` | `AdminLocationAccessID` INT | Junction table — local admin → store mapping |
| `SupportCase` | `SupportCaseID` INT | Status: Open/In Review/Resolved/Closed; Priority: Low/Medium/High; Category: Refund/Pickup/Account/Points/Order/Other |
| `SupportCaseNote` | `SupportCaseNoteID` INT | Internal notes on cases; FK→SupportCase, AdminUser |
| `AdminAuditLog` | `AdminAuditLogID` INT | Audit trail; `AAL_ActionType` e.g. `POINTS_ADJUST`; FK→AdminUser, Customer (nullable), Order (nullable), Location (nullable) |

**FK relationships (core):**
- `Order.CustomerID` → `Customer.CustomerID`
- `Order.LocationID` → `Location.LocationID`
- `Order.PaymentTypeID` → `PaymentType.PaymentTypeID`
- `OrderItem.OrderID` → `Order.OrderID`
- `OrderItem.ItemID` → `Item.ItemID`
- `JabberWonkTransaction.CustomerID` → `Customer.CustomerID`
- `JabberWonkTransaction.OrderID` → `Order.OrderID`

**Naming convention:** Non-key attributes are prefixed with a 3-letter table abbreviation (e.g., `CUS_`, `ITM_`, `LOC_`, `PAY_`, `ORD_`, `ORI_`, `JWT_`).

## Project Phases & Deadlines

| Phase | Tasks | Window | Status |
|---|---|---|---|
| Phase 1 | Timeline, Proposal, Conceptual Design, ERD, Data Dictionary | Mar 31 – Apr 6 | ✅ Complete |
| Phase 2 | Physical DB Design (Grayson & Andrew), Non-Functional Prototype (Warren & Adrian) | Apr 7 – Apr 20 | ✅ Complete |
| Phase 3 | SQL Queries (Grayson & Andrew), Functional DB & App (Adrian & Warren), User's Manual (Grayson) | Apr 21 – Apr 27 | 🔄 In Progress |
| Final | Presentation, Peer Evaluation, Deliverables (all members) | Apr 28 – Apr 30 | ⏳ Upcoming |

## Deliverable Files

| File | Purpose |
|---|---|
| `Support_Material/Echo_Team_Proposal.docx` | Project proposal |
| `Support_Material/Conceptual Design.drawio.png` | Conceptual ER model |
| `Support_Material/Echo_Data_Dictionary.xlsx` | Full data dictionary — **needs update** for new tables/columns (see Known Issues) |
| `Support_Material/Echo_Group_Timeline.xlsx` | Gantt chart / project schedule |
| `Support_Material/Non-Functional_Prototype.pptx` | UI mockups/wireframes |
| `Support_Material/JabberJuicy_Master_Schema.txt` | Authoritative SQL schema — kept in sync with live DB |
| `Support_Material/db_changes.txt` | Incremental SQL run against the live DB |
| `Support_Material/jabberjuicy_quotes.md` | Jabberwocky-themed quotes for each menu item |
| `Support_Material/remove_locations_gt11.sql` | Script to fix duplicate location rows (Known Issue #1) |
| `admin_db_sql.txt` | Admin schema additions — run after core schema to create 5 admin tables |
| `admin_implementation.md` | Architecture spec and feature map for admin dashboard |
| `_Deliverables/Admin_Dashboard_Wireframe.html` | Interactive HTML wireframe for admin dashboard |
| `_Deliverables/Instruction_Manual.docx` | User's manual (in progress — Grayson) |
| `WebAppWithDB_Starter_v2/` | ASP.NET Core Minimal API app (`Program.cs` + `DrinkQuotes.cs`) |
| `LOCAL_DEV.md` | Local development quick-start |

## App Architecture

Single-file ASP.NET Core Minimal API. All routes and handlers live in `Program.cs`. Business logic helpers (auth, cart, points, DB) are static private methods in the same `Program` class. `DrinkQuotes.cs` is the only other code file — a static dictionary mapping drink names to quotes.

Single-file ASP.NET Core Minimal API. All routes, handlers, helpers, and the background service live in `Program.cs`. `DrinkQuotes.cs` is the only other code file.

**Background service:** `ExpiredOrderCleanupService` is a `private sealed class` nested inside `Program`, registered as an `IHostedService`. It runs on a 2-hour timer and auto-cancels stale pending orders.

**Key helper methods (all in `Program.cs`):**

| Helper | Purpose |
|---|---|
| `UsernameExistsAsync` | Shared username uniqueness check (used by register + account change) |
| `GetPointsBalanceAsync` | Fetch `CUS_PointsBalance` for a customer |
| `GetJabberWonkPaymentTypeIdAsync` | Look up JWP payment type ID dynamically (never hardcoded) |
| `GetPendingOrderCountAsync` | Count pending orders for navbar badge |
| `HashPassword` / `VerifyPassword` | PBKDF2-SHA256, 310k iterations |
| `CancelOrderWithRefundAsync` | Shared cancel+JWP refund logic; uses `OUTPUT DELETED` to prevent double-refund |
| `FillDataTableViaCommandAsync` | Parameterized SELECT → DataTable |
| `ExecSqlCommandAsync` | Parameterized INSERT/UPDATE/DELETE |
| `IsAdminAuthenticated` / `SignInAdminSession` / `ClearAdminSession` | Admin session management (separate from customer session) |
| `BuildAdminUrl` | Constructs `/admin?...` query strings preserving grain/location/search state |
| `RowString` / `RowInt` / `RowDecimal` / `RowDate` | Safe null-checked DataRow column readers (admin helpers) |
| `GetAdminAccessibleLocationsAsync` | Returns locations the current admin can see (global = all, local = via AdminLocationAccess) |
| `SearchAdminCustomersAsync` | Customer search; scoped to accessible locations for non-global admins |

## JabberWonk Points Rules

| Rule | Value |
|---|---|
| Visit bonus (per qualifying order) | 50 pts |
| Spend bonus | 10 pts per whole dollar (floor) |
| Redemption rate | 100 pts = $1.00 |
| Earn on points-redeemed orders | 0 (no double-dip) |
| Cancellation refund | Full points refunded if order was paid with JWP |
| Per-visit earn cap | Max 2,500 pts earned per order (any excess is dropped) |

Constants are defined at the top of `Program.cs`: `JWP_PointsPerVisit`, `JWP_PointsPerDollar`, `JWP_PointsPerRedempt`, `JWP_MaxEarnPerVisit`.

**`JWT_TransactionType` values:** `EARN` (cash/card order completed), `REDEEM` (JWP order placed), `REFUND` (JWP order cancelled). The cancel handler looks up the original `REDEEM` row to get the exact points to restore.

**Earn cap math:** `earned = Min(JWP_PointsPerVisit + floor(total) * JWP_PointsPerDollar, JWP_MaxEarnPerVisit)`. Cap triggers on orders ≥ $245.

## Known Issues

1. **Duplicate locations in DB** — ✅ Resolved Apr 27. Duplicate rows (LocationIDs 5–12) manually removed directly in the DB. The `MIN(LocationID)` workaround in the checkout query can be cleaned up but is now a no-op.

2. **Data dictionary mismatch** — `Echo_Data_Dictionary.xlsx` still documents PKs as CHAR types (original design) and does not yet include `CUS_PointsBalance`, `JabberWonkTransaction`, or the JabberWonk Points PaymentType row. Needs manual update in Excel before final submission.

3. **Data dictionary missing new tables** — Same file needs new table entries for `JabberWonkTransaction` and the updated `Customer` schema, plus two new FK relationships on the PK-FK sheet.

4. **Custom domain HTTPS** — `jabberjuicy.com` DNS resolves but Railway has not auto-provisioned the HTTPS cert for the apex domain. `www.jabberjuicy.com` may have the same issue. Check Railway → Settings → Networking after next deploy.

## Session Notes (Apr 24, 2026) — Session 2: Admin Code Review + Fixes

- **Code review** of admin dashboard addition (acted as senior engineer reviewer against `admin_implementation.md` spec).
- **Fixed:** Parallelized all 13 independent DB queries in `HandleAdminDashboard` using `Task.WhenAll` — was sequential awaits, expected ~60–80% load time reduction.
- **Fixed:** `SearchAdminCustomersAsync` now scopes results to customers with at least one order at an accessible location for non-global admins. Signature updated to `(string q, int adminId, bool isGlobal, ...)`.
- **Added:** `POST /admin/points-adjust` → `HandleAdminPointsAdjust`. Atomically updates `CUS_PointsBalance` (CASE clamp ≥ 0 via `OUTPUT INSERTED`), writes an `AdminAuditLog` row (`POINTS_ADJUST` action type). Cannot write to `JabberWonkTransaction` because `OrderID` is `NOT NULL` and admin adjustments have no associated order.
- **Updated:** `RenderAdminPointsManager` — now accepts grain/metricRange/locationId/q for redirect-back URL, `adjAlert` for success/error display, and renders a delta + reason form that POSTs to `/admin/points-adjust`.
- **Updated:** `RenderAdminDashboardBody` signature — added `string adjAlert` param threaded from handler.
- Low-priority items from review saved to `TODO.md`.
- Changes not yet committed — all Apr 24 changes (both sessions) need a single commit and push.

## Session Notes (Apr 24, 2026) — Session 1: Background Service + Earn Cap

- **Added:** Auto-cancel background service (`ExpiredOrderCleanupService`, nested `BackgroundService` inside `Program`). Polls every 2 hours (12x/day), cancels any `Pending` order older than 60 minutes, refunds JWP if applicable. Registered via `builder.Services.AddHostedService<ExpiredOrderCleanupService>()`.
- **Refactored:** Extracted `CancelOrderWithRefundAsync(orderId, custId, logger, notes)` shared method. Uses `OUTPUT DELETED ... WHERE ORD_Status = 'Pending'` to atomically cancel and guard against double-refund race conditions. Both `HandlePickupCancel` and the background service call it.
- **Added:** `ORDER_EXPIRY_MINUTES = 60` and `ORDER_CLEANUP_INTERVAL = TimeSpan.FromHours(2)` constants at top of `Program.cs`.
- **Added:** `JWP_MaxEarnPerVisit = 2500` cap — points earned per order cannot exceed 2,500 regardless of order size. Applied via `Math.Min` in the checkout earn calculation.

## Session Notes (Apr 23, 2026)

- **Fixed:** `GET /pickup/{orderId}/success` route was missing from route registration — handler existed but was unreachable, causing a broken link after confirming pickup. Added `app.MapGet("/pickup/{orderId:int}/success", HandlePickupSuccess)`.
- **Added:** JWP points refund on order cancellation. `HandlePickupCancel` now checks if the cancelled order was paid with JabberWonk Points; if so, it looks up the original `REDEEM` transaction, restores the exact points to `CUS_PointsBalance`, and logs a `REFUND` entry in `JabberWonkTransaction`.
- Both fixes pushed to `origin/main` (commit `372cdaf`) and deployed to Railway.

## What's Next (Phase 3 Remaining)

- [ ] **Commit & push all Apr 24 changes** — `Program.cs` has uncommitted changes (background service, earn cap, admin module, code-review fixes). All moved/new support files also need staging.
- [x] **Apply admin DB schema to live DB** — `admin_db_sql.txt` applied and tested as of Apr 24. All 5 admin tables live and verified.
- [ ] SQL Queries deliverable (Grayson & Andrew) — analytical queries against the live DB
- [ ] User's Manual (Grayson) — `_Deliverables/Instruction_Manual.docx` drafted; needs review
- [ ] Update `Echo_Data_Dictionary.xlsx` with new schema changes (add `REFUND` transaction type, `CUS_PointsBalance`, `JabberWonkTransaction` table, JWP earn cap rule, 5 new admin tables)
- [x] Fix duplicate location rows in DB — manually resolved in DB (Apr 27)
- [ ] Test full end-to-end flow on live Railway deployment after Phase 3 push — include admin login at `/admin/login`
- [ ] Prepare presentation (all members, Apr 28–30)
