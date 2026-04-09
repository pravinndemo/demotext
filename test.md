Here’s a **JIRA-ready task** for the **actual agreed requirement** from the address discussion 👇

---

**Title**
Capture and persist correct X/Y coordinates during address selection / pin-on-map journey

**Summary**
Implement the agreed addressing change so that when a user creates or selects an address via the **Find Address / Pin on Map** journey, the system captures and saves the **correct X and Y coordinates** against the hereditament/address record.

This is the immediate scoped requirement discussed in the meeting. The broader addressing issues around UPRN generation, filtering, override behaviour, and end-to-end address lifecycle are noted separately and are not part of this task unless explicitly added.

---

**Background**
During the address issue discussion, it was clarified that the current problem is broader than GIS and spans Dynamics, GEO, DAL/API, and Postgres. However, the **immediate implementable requirement** is limited to ensuring that the **right X/Y coordinates are written back** based on user behaviour in the address selection flow.

The expected behaviour is:

* If the user **selects an existing address/record**, use the **existing record’s X/Y**
* If the user **clicks a location on the map**, use the **clicked map X/Y**
* If the user **overrides and creates a new address**, use the **override-derived X/Y** for that new address scenario

The system should determine which X/Y values to persist based on the path taken by the user. The user should not need to decide this manually.

---

**Business Requirement**
As a user creating or selecting an address,
I want the system to save the correct X/Y coordinates based on how I identified the property,
so that the hereditament has accurate geospatial data for downstream valuation, mapping, and integration processes.

---

**Scope**
This task includes:

* Capturing X/Y coordinates in the address journey
* Determining the correct X/Y source based on user action
* Persisting X/Y coordinates to the target record/system
* Exposing non-editable X/Y values in Dynamics if required by UI design

This task does **not** include, unless separately agreed:

* Full addressing process redesign
* UPRN redesign / removal of synthetic 13-digit UPRNs
* Full filtering logic correction
* Broader address cleansing in Dynamics
* Historical data backfill

---

**Functional Requirements**

1. When a user selects an **existing address record**, the system must persist the **X/Y coordinates from that selected record**.
2. When a user clicks a point on the map to define a location, the system must persist the **X/Y coordinates of that clicked point**.
3. When a user uses **override** to create a new address variant, the system must persist the **X/Y coordinates associated with that override scenario**, not blindly reuse another record’s coordinates.
4. The logic for choosing which X/Y values to save must be **system-driven**.
5. The saved X/Y values must be written back to the relevant downstream layer used by the hereditament/address record.
6. If shown in Dynamics/UI, X and Y fields should be **read-only / non-editable** for users.

---

**Acceptance Criteria**

### AC1 – Existing address selected

**Given** a user finds and selects an existing address record
**When** the address is saved/used
**Then** the system stores the X/Y coordinates from the selected existing record.

### AC2 – Map point selected

**Given** a user clicks a location on the map
**When** the address is saved/used
**Then** the system stores the X/Y coordinates from the clicked map location.

### AC3 – Override path used

**Given** a user uses the override option to create or amend an address
**When** the address is saved/used
**Then** the system stores the X/Y coordinates applicable to the override scenario.

### AC4 – System decides coordinate source

**Given** a user completes the address flow
**When** the record is persisted
**Then** the system automatically determines which X/Y source to use based on the user journey taken.

### AC5 – No manual editing of coordinates

**Given** X/Y coordinates are displayed in the UI
**When** the user views the address details
**Then** the coordinates are visible but not manually editable.

### AC6 – Downstream persistence

**Given** the address/hereditament is saved
**When** the transaction completes
**Then** the X/Y values are persisted to the relevant downstream store/service used by the address/hereditament process.

---

**Technical Notes**

* Impacted areas likely include:

  * GEO / Pin-on-Map component
  * Dynamics address UI
  * DAL / API layer
  * Postgres persistence layer
* Prefer minimal change aligned to current PI scope
* Avoid broad redesign in this story
* Any UPRN behaviour changes should be raised as separate stories

---

**Dependencies**

* Confirmation of target API / persistence endpoint
* Confirmation of source payload returned by GEO component
* Confirmation of Dynamics UI change for X/Y display fields, if needed

---

**Out of Scope / Follow-up Stories**

1. Review and fix address filtering logic causing valid records not to appear
2. Review synthetic UPRN generation approach
3. Review override journey end-to-end
4. Review address whitespace / formatting issues in Dynamics
5. End-to-end addressing design across GEO + Dynamics + DAL + Postgres

---

If you want, I can also turn this into:

* a **shorter dev-ready JIRA**
* a **BA-style story + subtasks**
* or **Epic + child stories** 🌟
