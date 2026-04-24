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

**Tables and primary keys:**

| Table | PK | Notes |
|---|---|---|
| `Customer` | `CustomerID` INT | Name, email, phone, full address, `CUS_PointsBalance` (JabberWonk pts) |
| `Item` | `ItemID` INT | Name, description, category, unit price, stock qty |
| `Location` | `LocationID` INT | Store address, phone, manager |
| `PaymentType` | `PaymentTypeID` INT | Cash, Credit Card, Debit Card, **JabberWonk Points** |
| `Order` | `OrderID` INT | Date/time, total, status (`Pending`/`Completed`/`Cancelled`), notes; FK→Customer, Location, PaymentType |
| `OrderItem` | `OrderItemID` INT | OrderID+ItemID line items; `ORI_Subtotal` is computed |
| `JabberWonkTransaction` | `JabberWonkTransactionID` INT | Points earn/redeem audit log; FK→Customer, Order; `JWT_` prefix |

**FK relationships:**
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
| `Echo_Team_Proposal.docx` | Project proposal |
| `Concept_drawing.drawio` / `Conceptual Design.drawio.png` | Conceptual ER model |
| `Echo_Data_Dictionary.xlsx` | Full data dictionary — **needs update** for new tables/columns (see Known Issues) |
| `Echo_Group_Timeline.xlsx` | Gantt chart / project schedule |
| `User_Interface.pptx` | UI mockups/wireframes |
| `BusinessRules.docx` | Business rules document |
| `JabberJuice_Reference.docx` | Reference material for the JabberJuicy business |
| `JabberJuicy_Master_Schema.txt` | Authoritative SQL schema — kept in sync with live DB |
| `db_changes.txt` | Incremental SQL run against the live DB |
| `jabberjuicy_quotes.md` | Jabberwocky-themed quotes for each menu item |
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

1. **Duplicate locations in DB** — A re-run of the location seed script created duplicate rows (LocationIDs 5–12 mirror 1–4). Real orders exist on the duplicate IDs so a simple DELETE is blocked by FK constraint. Workaround in app: `MIN(LocationID)` GROUP BY in the checkout location query. **Fix before final deployment:** UPDATE Orders on duplicate IDs to point to original IDs, then DELETE duplicates.

2. **Data dictionary mismatch** — `Echo_Data_Dictionary.xlsx` still documents PKs as CHAR types (original design) and does not yet include `CUS_PointsBalance`, `JabberWonkTransaction`, or the JabberWonk Points PaymentType row. Needs manual update in Excel before final submission.

3. **Data dictionary missing new tables** — Same file needs new table entries for `JabberWonkTransaction` and the updated `Customer` schema, plus two new FK relationships on the PK-FK sheet.

4. **Custom domain HTTPS** — `jabberjuicy.com` DNS resolves but Railway has not auto-provisioned the HTTPS cert for the apex domain. `www.jabberjuicy.com` may have the same issue. Check Railway → Settings → Networking after next deploy.

## Session Notes (Apr 24, 2026)

- **Added:** Auto-cancel background service (`ExpiredOrderCleanupService`, nested `BackgroundService` inside `Program`). Polls every 2 hours (12x/day), cancels any `Pending` order older than 60 minutes, refunds JWP if applicable. Registered via `builder.Services.AddHostedService<ExpiredOrderCleanupService>()`.
- **Refactored:** Extracted `CancelOrderWithRefundAsync(orderId, custId, logger, notes)` shared method. Uses `OUTPUT DELETED ... WHERE ORD_Status = 'Pending'` to atomically cancel and guard against double-refund race conditions. Both `HandlePickupCancel` and the background service call it.
- **Added:** `ORDER_EXPIRY_MINUTES = 60` and `ORDER_CLEANUP_INTERVAL = TimeSpan.FromHours(2)` constants at top of `Program.cs`.
- **Added:** `JWP_MaxEarnPerVisit = 2500` cap — points earned per order cannot exceed 2,500 regardless of order size. Applied via `Math.Min` in the checkout earn calculation.
- Changes not yet committed — commit and push when ready to deploy.

## Session Notes (Apr 23, 2026)

- **Fixed:** `GET /pickup/{orderId}/success` route was missing from route registration — handler existed but was unreachable, causing a broken link after confirming pickup. Added `app.MapGet("/pickup/{orderId:int}/success", HandlePickupSuccess)`.
- **Added:** JWP points refund on order cancellation. `HandlePickupCancel` now checks if the cancelled order was paid with JabberWonk Points; if so, it looks up the original `REDEEM` transaction, restores the exact points to `CUS_PointsBalance`, and logs a `REFUND` entry in `JabberWonkTransaction`.
- Both fixes pushed to `origin/main` (commit `372cdaf`) and deployed to Railway.

## What's Next (Phase 3 Remaining)

- [ ] **Commit & push Apr 24 changes** — `Program.cs` has uncommitted changes (background service + earn cap)
- [ ] SQL Queries deliverable (Grayson & Andrew) — analytical queries against the live DB
- [ ] User's Manual (Grayson) — `_Deliverables/Instruction_Manual.docx` drafted; needs review
- [ ] Update `Echo_Data_Dictionary.xlsx` with new schema changes (add `REFUND` transaction type, `CUS_PointsBalance`, `JabberWonkTransaction` table, JWP earn cap rule)
- [ ] Fix duplicate location rows in DB (see Known Issue #1)
- [ ] Test full end-to-end flow on live Railway deployment after Phase 3 push
- [ ] Prepare presentation (all members, Apr 28–30)
