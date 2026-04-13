
---

## **Task 1: Investigate consequential process for automated request and job creation**

**Description**
Perform a detailed technical investigation of the existing **Consequential process** to understand how requests and jobs are automatically created within the current system.

The objective is to identify all technical components, execution paths, and dependencies involved in the automation so that the same patterns can be reused or adapted for the upcoming **Bulk Work Processor / Bulk Request-Job creation design**.

The analysis should trace the complete flow starting from the UI or trigger point through to request creation, job creation, and any subsequent routing or allocation.

Areas to be covered include:

* Entry point of the consequential process (UI / custom page / PCF / backend trigger)
* Dataverse entities/tables involved in the process
* Custom APIs used for request or job creation
* Plugins (including pre/post operation steps) involved in the flow
* JavaScript logic executed on forms or custom pages
* Power Automate flows triggered during or after creation
* Azure Functions or external integrations (if any)
* Use of queues (Service Bus / Storage Queue) if applicable
* Logic used for request validation (if bypassed or automated)
* Mechanism used to create jobs from requests
* How request and job are linked
* Team allocation / routing logic triggered as part of the process

The output should clearly identify:

* What logic is reused vs custom-built
* Which parts of the current system can be reused for bulk processing
* Which parts are tightly coupled or inefficient and should be avoided

The findings should be documented and shared with the team via a walkthrough session and/or technical documentation.

---

## **Task 2: Document end-to-end flow from request creation to job linkage (Data Enhancement)**

**Description**
Document the complete end-to-end technical flow for **request creation through to job creation and linkage**, specifically focusing on the **Data Enhancement job type**.

The goal is to establish a clear understanding of how the current system behaves so that the future bulk processing solution can replicate the required behavior without relying on manual UI steps such as “Validate Request”.

The analysis should cover the full lifecycle from user interaction to system-generated outputs, including all required attributes, validations, and system logic.

Areas to be covered include:

* Request creation process:

  * Required fields and attributes for creating a valid request
  * Default values and auto-populated fields
  * Form-level JavaScript logic influencing request data
  * Any hidden or system-driven attributes

* Validation logic:

  * What happens during “Validate Request”
  * Which rules are applied (client-side vs server-side)
  * Whether validation calls Custom APIs or plugins
  * What minimum criteria are required for job creation

* Job creation logic:

  * How the system creates a **Data Enhancement job**
  * Which component triggers job creation (plugin / API / flow)
  * What data is passed from request to job
  * How job type influences behavior

* Request ↔ Job linkage:

  * How the job is linked back to the request
  * Any additional related records created
  * Relationship structure between entities

* Status and lifecycle:

  * Initial statuses for request and job
  * Any transitions triggered automatically
  * BPF (if applicable) or stage initialization

* Team allocation and routing:

  * How jobs are assigned to teams
  * Rules/configurations used for routing (job type, location, BA, etc.)
  * Any Power Automate or plugin logic involved
  * Whether assignment is automatic or manual at this stage

* Additional considerations:

  * Any dependencies on PAD, hereditament, or related entities
  * Any constraints or validations specific to Data Enhancement
  * Performance or system limitations observed in current flow

The outcome should be a clear, structured documentation of the current-state process that can be used as a baseline for designing and implementing the **Bulk Work Processor**, ensuring that request and job creation can be automated correctly without relying on manual intervention.
