Here are **clear, HMRC-style Jira tasks** specifically for a **Dynamics (Dataverse) developer** based on your VSC requirement 👇

---

## 🧾 **Task 1: Analyse Current VSC Implementation in PAD Flow**

### **Description**

Perform a detailed technical analysis of how **VSC (Valuation Scenario Code)** is currently implemented within the PAD (Property Attribute Data) workflow in Dynamics.

The objective is to understand:

* How VSC codes are stored and retrieved (reference data / virtual tables / Dataverse entities)
* How VSC is surfaced in the PAD UI (forms, subgrids, controls)
* How VSC values are saved and associated with the NDA/PAD attribute set
* Whether there are any dependencies between:

  * Dwelling Group
  * Dwelling Type
  * VSC Code
* How the **VSC factor** is handled:

  * Default value behaviour (e.g., default = 1)
  * Any plugin, JavaScript, or PCF logic modifying it
* API/DAL interaction:

  * Identify the API calls used to persist and retrieve VSC codes
  * Understand how VSC is linked to PAD records via NDA Set ID

Deliverables:

* Summary of current VSC data model and flow
* List of impacted components (forms, JS, plugins, APIs, virtual entities)
* Confirmation whether VSC is purely reference-data-driven or has custom logic dependencies

---

## 🧾 **Task 2: Validate and Enable New VSC Codes in PAD UI**

### **Description**

Ensure that newly introduced VSC codes (e.g., student cluster accommodation, agriculture variants, leisure/retail composites, annex/parent, cladding-related codes) are correctly surfaced and usable within the existing PAD workflow in Dynamics.

Activities include:

* Verify that new VSC codes added in reference/master data are:

  * Available in the VSC selection control in PAD forms
  * Correctly displayed with code and description
* Validate that users can:

  * Select new VSC codes
  * Save them against PAD records
  * Retrieve them correctly on reload
* Confirm that VSC codes are:

  * Properly associated with NDA attribute sets
  * Persisted through existing API/DAL integration
* Ensure no UI or validation issues occur when selecting new VSC codes

Also verify:

* No unintended restrictions exist on VSC selection (e.g., filtering by dwelling group/type)
* Existing behaviour remains unchanged for current VSC codes

Deliverables:

* Validation results confirming end-to-end functionality
* List of any required configuration or code fixes
* Screenshots or evidence of successful VSC selection and persistence

---

## 🧾 **Task 3: Review and Validate VSC Factor Handling in Dynamics**

### **Description**

Analyse and validate how the **VSC factor** is handled within Dynamics and confirm whether any changes are required to support new VSC codes.

Activities:

* Identify where the VSC factor is:

  * Stored (reference data / virtual table / API response)
  * Displayed in the UI (if applicable)
* Review current behaviour:

  * Default factor value (e.g., set to 1)
  * Any logic in:

    * Plugins
    * JavaScript
    * PCF controls
* Validate whether:

  * Factor is editable by users
  * Factor is overridden or recalculated anywhere in the system
* Confirm that new VSC codes:

  * Can carry factor values from reference data
  * Do not break existing behaviour

Note:
No changes should be made unless explicitly required by business rules.

Deliverables:

* Technical note on current factor handling
* Confirmation whether changes are required or not
* Identification of any risks or inconsistencies

---

## 🧾 **Task 4: Assess Impact and Approach for VSC Backfill on Existing PAD Records**

### **Description**

Assess how existing PAD records can be updated (backfilled) with new or revised VSC codes and identify the appropriate technical approach within Dynamics.

Activities:

* Analyse how VSC is currently stored against PAD/NDA records
* Identify:

  * Whether existing PAD records can be updated via:

    * Data Enhancement jobs
    * Direct data update (script/API)
* Evaluate impact on:

  * Existing PAD records
  * Audit/history behaviour
  * Validation and release process
* Confirm whether Dynamics supports bulk update through:

  * Existing workflows
  * APIs
  * Or requires external data migration

Deliverables:

* Recommended approach for backfill (Dynamics vs external script)
* List of impacted entities and fields
* Risks and constraints (e.g., validation dependencies, release process)

---

## 🧾 **Task 5: Validate End-to-End PAD Save and Release Behaviour with New VSC Codes**

### **Description**

Ensure that introducing new VSC codes does not impact the existing PAD lifecycle, including validation, progression, and release.

Activities:

* Create/update PAD records with new VSC codes
* Validate:

  * PAD validation step (pass/fail/warnings)
  * Progression through stages (e.g., research → validation → release)
* Confirm that:

  * VSC codes persist through all stages
  * Associated child records (VSC, source code, remarks) are correctly released
* Verify behaviour for:

  * “No Action” scenarios (if PAD is not fully processed)
  * Reopening and editing PAD records

Deliverables:

* Test results confirming no regression in PAD lifecycle
* Identification of any issues or required fixes

---

If you want next step, I can also:
👉 break these into **subtasks (plugin / JS / DAL / testing split)**
👉 or map them to **solution layers (Dynamics vs DAL vs DAP2)** for your design discussion 🚀
