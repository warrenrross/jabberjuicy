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

**Tables and primary keys:**

| Table | PK | Notes |
|---|---|---|
| `Customer` | `CustomerID` (CHAR 8) | Name, email, phone, full address |
| `Item` | `ItemID` (CHAR 8) | Name, description, category, unit price, stock qty |
| `Location` | `LocationID` (CHAR 8) | Store address, phone, manager |
| `PaymentType` | `PaymentTypeID` (CHAR 4) | e.g. Cash, Credit Card, Debit Card |
| `Order` | `OrderID` (CHAR 10) | Date/time, total, status, notes; FK→Customer, Location, PaymentType |
| `OrderItem` | `OrderID`+`ItemID` (composite PK) | Quantity, unit price at time of order, subtotal |

**FK relationships:**
- `Order.CustomerID` → `Customer.CustomerID` (many orders per customer)
- `Order.LocationID` → `Location.LocationID` (many orders per location)
- `Order.PaymentTypeID` → `PaymentType.PaymentTypeID`
- `OrderItem.OrderID` → `Order.OrderID`
- `OrderItem.ItemID` → `Item.ItemID`

**Naming convention:** Non-key attributes are prefixed with a 3-letter table abbreviation (e.g., `CUS_`, `ITM_`, `LOC_`, `PAY_`, `ORD_`, `ORI_`).

## Project Phases & Deadlines

| Phase | Tasks | Window |
|---|---|---|
| Phase 1 (complete) | Timeline, Proposal, Conceptual Design, ERD, Data Dictionary | Mar 31 – Apr 6 |
| Phase 2 | Physical DB Design (Grayson & Andrew), Non-Functional Prototype (Warren & Adrian) | Apr 7 – Apr 20 |
| Phase 3 | SQL Queries (Grayson & Andrew), Functional DB & App (Adrian & Warren), User's Manual (Grayson) | Apr 21 – Apr 27 |
| Final | Presentation, Peer Evaluation, Deliverables (all members) | Apr 28 – Apr 30 |

## Deliverable Files

| File | Purpose |
|---|---|
| `Echo_Team_Proposal.docx` | Project proposal |
| `Concept_drawing.drawio` / `Conceptual Design.drawio.png` | Conceptual ER model |
| `Echo_Data_Dictionary.xlsx` | Full data dictionary with PK/FK relationship sheet |
| `Echo_Group_Timeline.xlsx` | Gantt chart / project schedule |
| `User_Interface.pptx` | UI mockups/wireframes |
| `BusinessRules.docx` | Business rules document |
| `JabberJuice_Reference.docx` | Reference material for the JabberJuicy business |
| `branding_jabberjuicy.png` | Brand/logo asset |
