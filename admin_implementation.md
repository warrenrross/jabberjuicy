# Admin Dashboard Implementation Plan

This document describes how the current admin wireframe in [_Deliverables/Admin_Dashboard_Wireframe.html](/Users/warrenrross/Education/DASC/DASC_Data_Management/GroupProjectEchoTeam/_Deliverables/Admin_Dashboard_Wireframe.html) should be implemented from a systems architecture point of view.

It is written for the current JabberJuicy stack:

- ASP.NET Core Minimal API
- Single main app file at [Program.cs](/Users/warrenrross/Education/DASC/DASC_Data_Management/GroupProjectEchoTeam/WebAppWithDB_Starter_v2/WebAppWithDB_Starter_v2/Program.cs)
- SQL Server relational database
- Server-side session authentication
- Raw parameterized `SqlCommand` queries

## 0. Approved Decisions

These decisions are now selected for the admin build:

- Use the long-term separate admin solution instead of putting admin flags on `Customer`.
- Add a dedicated `AdminUser` table and admin-only session keys.
- Support both access models:
  - global access with `ADM_IsGlobalAccess = 1`
  - local access with rows in `AdminLocationAccess`
- Add a temporary navbar pill labeled `Demo Admin` in red text that links to `/admin/login`.
- Seed one demo global admin account with username `JabberWocky` and password `Bandersnatch!`.
- Keep the demo admin insert script in a separate local-only file, `insert_sql.txt`, and exclude it from git.

## 1. Architecture Direction

The admin dashboard should be implemented as an **admin module inside the existing web app**, not as a separate service.

That is the best fit for this project because:

- the app is small and already uses one deployment unit
- the admin screens need direct access to the same order, customer, item, and loyalty data
- splitting into separate services would add complexity without much benefit for a course project

Recommended structure:

- `GET /admin` serves the main admin dashboard page
- `GET /admin/...` serves admin subpages if needed later
- `GET /admin/api/...` returns JSON for dashboard widgets
- `POST /admin/api/...` handles admin actions like points adjustments or case updates

Even if the first version stays in `Program.cs`, the admin logic should be grouped into separate regions or helper methods so it does not mix with customer checkout flow code.

## 2. Current System Constraints

The current app already supports:

- customer login using sessions
- orders, order items, locations, payment types, and loyalty transactions
- loyalty balance reads and points refund logic

The current app does **not** yet support:

- admin identity and role-based authorization
- persistent admin case handling notes
- audit trails for manual admin actions
- JSON endpoints dedicated to dashboard metrics

Because of that, the admin dashboard should be built in two layers:

1. A UI layer for the dashboard pages and components
2. A supporting admin data layer for roles, case records, and audit logs

## 3. Recommended Admin Data Model

Some dashboard features can be built from existing tables, but a few need new tables to be done cleanly.

### 3.1 New Tables

#### `AdminUser`

Purpose:

- authenticate employees/admins separately from customers
- support roles like `Admin` and `Manager`

Selected columns:

- `AdminUserID` INT IDENTITY PK
- `ADM_Username`
- `ADM_PasswordHash`
- `ADM_DisplayName`
- `ADM_Role`
- `ADM_IsActive`
- `ADM_IsGlobalAccess`
- `ADM_IsDemoUser`
- `ADM_LastLoginAt`

#### `AdminLocationAccess`

Purpose:

- restrict which store locations an admin can see or manage

Recommended columns:

- `AdminLocationAccessID` INT IDENTITY PK
- `AdminUserID` FK
- `LocationID` FK

#### `SupportCase`

Purpose:

- persist customer service issues that the admin works from the combined customer lookup/workspace panel

Recommended columns:

- `SupportCaseID` INT IDENTITY PK
- `CustomerID` FK nullable
- `OrderID` FK nullable
- `LocationID` FK nullable
- `SC_Status` such as `Open`, `In Review`, `Resolved`, `Closed`
- `SC_Priority` such as `Low`, `Medium`, `High`
- `SC_Category` such as `Refund`, `Pickup`, `Account`, `Points`
- `SC_Subject`
- `SC_Description`
- `SC_CreatedAt`
- `SC_ResolvedAt` nullable
- `CreatedByAdminUserID` FK
- `AssignedToAdminUserID` FK nullable

#### `SupportCaseNote`

Purpose:

- store internal resolution notes, follow-ups, and action history

Recommended columns:

- `SupportCaseNoteID` INT IDENTITY PK
- `SupportCaseID` FK
- `AdminUserID` FK
- `SCN_NoteText`
- `SCN_CreatedAt`

#### `AdminAuditLog`

Purpose:

- record sensitive admin actions for traceability

Recommended columns:

- `AdminAuditLogID` INT IDENTITY PK
- `AdminUserID` FK
- `AAL_ActionType`
- `AAL_EntityType`
- `AAL_EntityID`
- `AAL_Details`
- `AAL_CreatedAt`

Examples of actions to log:

- manual points adjustment
- case status change
- refund approval
- order status override

### 3.2 Existing Tables Used by Admin Features

The dashboard should continue to use existing production tables as its operational source of truth:

- `Customer`
- `Item`
- `Location`
- `PaymentType`
- `[Order]`
- `OrderItem`
- `JabberWonkTransaction`

## 4. Authorization and Session Design

Admin access should not reuse normal customer sessions.

Selected approach:

- add a dedicated admin login route such as `GET /admin/login` and `POST /admin/login`
- store admin session keys like `admin_uid`, `admin_name`, `admin_role`
- protect admin routes with a helper such as `RequireAdmin(HttpContext ctx)`

Recommended rules:

- customers cannot access `/admin`
- admins cannot perform write actions without an active admin session
- location-scoped admins should only see permitted stores

Chosen data boundary:

- do **not** add `CUS_IsAdmin` to `Customer`
- keep admins separate in `AdminUser`

This keeps employee authorization independent from customer identity and avoids mixing public customer records with staff accounts.

## 5. API and Page Architecture

### 5.1 Page Layer

Initial pages:

- `GET /admin/login`
- `POST /admin/login`
- `GET /admin`

Presentation support:

- show a `Demo Admin` navbar pill so evaluators can find the admin entry point quickly

Optional later pages:

- `GET /admin/customers/{id}`
- `GET /admin/orders/{id}`
- `GET /admin/cases/{id}`

### 5.2 JSON Endpoints

The dashboard widgets should be fed by small JSON endpoints rather than one giant all-in-one payload.

Benefits:

- easier debugging
- easier incremental loading
- cleaner separation between widgets
- lower risk of one query breaking the entire dashboard

Recommended endpoint set:

- `GET /admin/api/customer-search`
- `GET /admin/api/customers/{id}`
- `GET /admin/api/customers/{id}/cases`
- `POST /admin/api/cases`
- `POST /admin/api/cases/{id}/notes`
- `POST /admin/api/cases/{id}/status`
- `GET /admin/api/points/customer/{id}`
- `POST /admin/api/points/adjust`
- `GET /admin/api/orders/live`
- `POST /admin/api/orders/{id}/status`
- `GET /admin/api/metrics/sales-trend`
- `GET /admin/api/metrics/activity`
- `GET /admin/api/metrics/popular-items`
- `GET /admin/api/metrics/payment-mix`
- `GET /admin/api/metrics/location-performance`

## 6. Feature-by-Feature Implementation

The current wireframe has eight numbered features.

### 6.1 Feature 1: Customer Lookup and Case Workspace

Purpose:

- search for a customer or order
- open that customer’s profile
- view related orders, points history, and support cases
- create or update a support case in the same workspace

#### Read sources

- `Customer`
- `[Order]`
- `OrderItem`
- `JabberWonkTransaction`
- `SupportCase`
- `SupportCaseNote`

#### Write sources

- `SupportCase`
- `SupportCaseNote`
- `AdminAuditLog`

#### Recommended endpoints

- `GET /admin/api/customer-search?q=...`
- `GET /admin/api/customers/{id}`
- `GET /admin/api/customers/{id}/cases`
- `POST /admin/api/cases`
- `POST /admin/api/cases/{id}/notes`
- `POST /admin/api/cases/{id}/status`

#### Search behavior

Allow searching by:

- customer name
- username
- email
- phone
- order ID

Use parameterized `LIKE` queries and exact match for `OrderID`.

#### System behavior

- selecting a customer loads profile, recent orders, points balance, and open cases
- selecting an order loads order summary and line items
- admins can add notes or open a new case from this panel

#### Architecture note

This feature replaces the removed separate issue center by making the customer workspace the primary service console.

### 6.2 Feature 2: JabberWonk Points Manager

Purpose:

- view a customer’s full loyalty transaction history
- manually add or subtract points when needed
- support refund-related adjustments

#### Read sources

- `Customer.CUS_PointsBalance`
- `JabberWonkTransaction`

#### Write sources

- `Customer`
- `JabberWonkTransaction`
- `AdminAuditLog`

#### Recommended endpoints

- `GET /admin/api/points/customer/{id}`
- `POST /admin/api/points/adjust`

#### Write flow

Manual adjustment should happen in a transaction:

1. read current points balance
2. calculate new balance
3. update `Customer.CUS_PointsBalance`
4. insert a `JabberWonkTransaction` row such as `ADJUST`
5. insert `AdminAuditLog`

#### Architecture note

This should reuse the same points rules and payment type lookup logic already present in `Program.cs`, rather than creating a second loyalty rules engine.

### 6.3 Feature 3: Live Order Queue

Purpose:

- show orders currently active in the system
- monitor pickup delays and cancellations

#### Read sources

- `[Order]`
- `Customer`
- `Location`
- `PaymentType`

#### Write sources

- `[Order]`
- `AdminAuditLog`

#### Recommended endpoints

- `GET /admin/api/orders/live?locationId=...`
- `POST /admin/api/orders/{id}/status`

#### Query behavior

Default to:

- `Pending`
- `Completed` from the recent window
- `Cancelled` from the recent window

Include:

- customer name
- order total
- payment type
- location
- age of order

#### Refresh strategy

- poll every 30 to 60 seconds
- keep this widget lightweight

#### Architecture note

This panel is operational, so it should query the live transaction tables directly rather than a reporting cache.

### 6.4 Feature 4: Sales Trend with Day, Hour, and Week Views

Purpose:

- show revenue and order counts over a selected time grain

#### Read sources

- `[Order]`

#### Recommended endpoint

- `GET /admin/api/metrics/sales-trend?grain=day|hour|week&locationId=...`

#### Query behavior

- `hour`: group by hour for the current day
- `day`: group by date for the current rolling window
- `week`: group by week start date

Only count completed revenue by default. Cancelled orders should be excluded or shown separately.

#### Response shape

Return a compact JSON array:

```json
[
  { "bucket": "2026-04-24 09:00", "sales": 184.50, "orders": 14 },
  { "bucket": "2026-04-24 10:00", "sales": 236.75, "orders": 18 }
]
```

#### Architecture note

For this project size, on-demand aggregation is fine. If the dataset grows later, this can move to a summarized reporting table.

### 6.5 Feature 5: Recent Activity Feed

Purpose:

- show recent events across customer, order, and loyalty activity

#### Read sources

- `Customer`
- `[Order]`
- `JabberWonkTransaction`
- `SupportCase`
- `AdminAuditLog`

#### Recommended endpoint

- `GET /admin/api/metrics/activity?limit=25`

#### Feed event examples

- new customer registered
- order placed
- order cancelled
- points redeemed
- points refunded
- support case opened
- manual admin adjustment

#### Architecture note

The easiest implementation is to compose this feed from multiple small recent queries and merge them in memory into a timestamp-sorted list.

### 6.6 Feature 6: Most Popular Items

Purpose:

- show which menu items are currently performing best

#### Read sources

- `OrderItem`
- `[Order]`
- `Item`
- `Location`

#### Recommended endpoint

- `GET /admin/api/metrics/popular-items?range=today|week&locationId=...`

#### Query behavior

Join `OrderItem` to completed orders and aggregate by `ItemID`.

Useful measures:

- units sold
- revenue contribution
- repeat purchase count if available
- top location for the item

#### Architecture note

This is a reporting feature, so it should only count orders that reached a completed state unless the UI explicitly asks for placed orders.

### 6.7 Feature 7: Payment and Loyalty Mix

Purpose:

- show how customers are paying
- compare cash/card volume against JabberWonk usage

#### Read sources

- `[Order]`
- `PaymentType`
- `JabberWonkTransaction`

#### Recommended endpoint

- `GET /admin/api/metrics/payment-mix?range=today|week&locationId=...`

#### Metrics to return

- order count by payment type
- sales amount by payment type
- points earned in range
- points redeemed in range
- percentage of orders using JabberWonk Points

#### Architecture note

This should be one backend endpoint because the payment bars and loyalty counters are semantically related and will usually refresh together.

### 6.8 Feature 8: Store Performance by Location

Purpose:

- compare locations on revenue and operational efficiency

#### Read sources

- `[Order]`
- `Location`
- `PaymentType`

#### Recommended endpoint

- `GET /admin/api/metrics/location-performance?range=today|week`

#### Metrics to calculate

- revenue per location
- order count per location
- cancellation rate per location
- average pickup completion time if timestamps are available

#### Important limitation

The known duplicate `Location` rows in the database can distort this widget.

Before this feature is treated as reliable, the duplicate location issue documented in [AGENTS.md](/Users/warrenrross/Education/DASC/DASC_Data_Management/GroupProjectEchoTeam/AGENTS.md) should be fixed in the live database.

## 7. Cross-Cutting Technical Requirements

### 7.1 Database Transactions

Any admin write action that changes balances, statuses, or support records should run inside SQL transactions.

Examples:

- points adjustments
- refund processing
- order status override
- case create plus initial note

### 7.2 Parameterized Queries

All admin queries must continue using parameterized `SqlCommand` objects.

Do not build SQL by concatenating search text, order IDs, usernames, or category values directly into the query string.

### 7.3 Audit Logging

Every privileged write action should create an `AdminAuditLog` row.

Minimum fields to capture:

- who performed the action
- what entity changed
- old and new values when practical
- when it happened

### 7.4 Error Handling

Admin endpoints should return:

- user-safe error messages to the UI
- detailed internal logs to the server console

The admin dashboard should fail widget-by-widget when possible. One broken metric should not blank out the entire admin screen.

### 7.5 Performance

Recommended initial strategy:

- live queries for queue and customer lookups
- aggregated queries for metrics
- client polling for operational widgets only

Avoid refreshing everything every few seconds. Suggested pattern:

- live queue: 30 to 60 sec
- activity feed: 60 sec
- metrics: on page load plus manual refresh

## 8. Recommended Indexes

To keep admin queries responsive, add indexes for the expected access patterns.

Recommended indexes:

- `[Order](ORD_OrderDate)`
- `[Order](ORD_Status, ORD_OrderDate)`
- `[Order](CustomerID, ORD_OrderDate)`
- `[Order](LocationID, ORD_OrderDate)`
- `OrderItem(OrderID)`
- `OrderItem(ItemID)`
- `JabberWonkTransaction(CustomerID, JWT_TransactionDate)`
- `SupportCase(CustomerID, SC_Status, SC_CreatedAt)`
- `SupportCase(OrderID, SC_Status)`

## 9. Suggested Implementation Order

### Phase 1: Admin foundation

- add admin auth
- add `AdminUser`
- add protected `/admin`
- add basic dashboard shell

### Phase 2: Operational tools

- customer lookup and workspace
- points manager
- live order queue
- audit logging

### Phase 3: Reporting widgets

- sales trend
- recent activity feed
- most popular items
- payment and loyalty mix
- location performance

### Phase 4: Hardening

- location-based permissions
- improved error states
- indexing
- final UI cleanup

## 10. Recommended Project-Level Decision

If the team wants the cleanest implementation, the admin dashboard should be treated as a **new bounded area of the app** with its own:

- login flow
- session keys
- helper methods
- data access methods
- audit rules

That keeps admin workflows from leaking into the customer-facing order flow and makes the system easier to explain in the final project presentation.
