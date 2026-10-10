---
name: fhir
description: FHIR (healthcare interoperability) for .NET with the Firely SDK (Hl7.Fhir.*). Resources, references, profiles, RESTful search, FHIRPath, and FHIR Messaging for national exchange platforms. Use R4. Per-project skill: drop into a repo that integrates FHIR.
---

# FHIR — healthcare interoperability for .NET

FHIR (Fast Healthcare Interoperability Resources) is the HL7 standard for exchanging healthcare data.
Relevant to a healthcare platform, but only in the services that actually speak FHIR — so it's a
per-project skill.

> Per-project skill. Copy into a repo's `.claude/skills/` only for a service that integrates FHIR.
> Kept out of the global set to preserve skill-selection budget.

## Use the Firely SDK
Use the **Firely .NET SDK** (`Hl7.Fhir.*`) — the official, mature library maintained by the standard's
own team. It gives you strongly-typed resource classes, JSON/XML serialization, validation, profiling,
FHIRPath evaluation, and a RESTful client. Don't hand-roll FHIR JSON.

## Version: R4 for production
- **FHIR R4** is normative and the production target — ~95%+ of certified healthcare systems are on it.
- R5 exists but is trial-use / early-adopter; STU3 is legacy.
- **Critical:** the SDK package version must match your target FHIR version — STU3/R4/R4B/R5 have
  breaking model differences, and mixing them silently produces wrong resources. Pin the R4 package
  (`Hl7.Fhir.R4`).

## Core concepts
- **Everything is a Resource** — `Patient`, `Observation`, `Encounter`, `Condition`, `MedicationRequest`,
  etc. Each is a strongly-typed class in the SDK.
- **Resources link by reference** — e.g. an `Observation.Subject` references a `Patient` by
  `Patient/{id}`, not by embedding. Resolve references via the client when you need the linked resource.
- **Profiles constrain FHIR** — base FHIR is deliberately flexible; national/organizational **profiles**
  (US Core, or a national profile) tighten which fields are required, allowed value sets, cardinality.
  Validate against the profile you must conform to, not just base FHIR.

## RESTful interaction (the FHIR API)
FHIR is a REST API with a defined interaction set:
```csharp
var client = new FhirClient("https://fhir-server/fhir");   // R4 client
var patient = await client.ReadAsync<Patient>("Patient/123");
var created = await client.CreateAsync(newPatient);
var bundle  = await client.SearchAsync<Observation>(new[] { "subject=Patient/123", "category=vital-signs" });
```
- **CRUD**: read / create / update / delete by resource type + id.
- **Search**: query parameters per resource; supports **chained search** (`subject.name=...`) and
  `_include` / `_revinclude` to pull linked resources in one round-trip. Results come back as a
  `Bundle`.

## FHIRPath
A path expression language for navigating/extracting from resources
(`Patient.name.where(use='official').family`). The SDK evaluates it — useful for validation rules and
pulling values without manual null-walking.

## National exchange platforms use Messaging, not REST

Many national health-exchange platforms (insurance claims, eligibility, referrals) use FHIR **Messaging**:
a `Bundle` of type `message` with a `MessageHeader` first, posted to one endpoint with organisation-level
PKI auth, and validated against the platform's own profiles. The REST `FhirClient` CRUD/search flow
above doesn't apply to them. Model request/response correlation on `Bundle.identifier` and
`MessageHeader.response.identifier`, and validate against the platform's implementation guide, not
base FHIR. A team that integrates one keeps those details in its own layer.

## Rules
- Firely SDK, R4, package version pinned to the FHIR version — never mix versions.
- Validate against the specific **profile** you must conform to, not just base FHIR.
- Resources reference each other by `Type/id`; use `_include`/chained search to avoid N round-trips.
- Treat PHI in FHIR resources under the same healthcare-compliance rules as the rest of the platform
  (audit, encryption, no PHI in logs).
