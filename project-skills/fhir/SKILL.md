---
name: fhir
description: FHIR (healthcare interoperability) for .NET with the Firely SDK 6 (Hl7.Fhir.R4) — R4 resources, JSON with the current deserializer, FHIRPath, and FHIR Messaging for national exchange platforms (header first, matching responses to requests). Per-project skill. Messaging, parsing and FHIRPath tested in CI.
version: 2.0.0
---

# FHIR: healthcare interoperability for .NET

FHIR is the HL7 standard for exchanging healthcare data. Only the services that speak FHIR need it, so
this is a per-project skill: copy it into those repos only.

## The SDK and the version
- **Firely .NET SDK** (`Hl7.Fhir.R4`, BSD-3), from the team behind the standard: typed resources, JSON
  and XML, FHIRPath, a REST client. Don't hand-roll FHIR JSON.
- **R4** is what production systems run; R5 is early-adopter, STU3 legacy. The package fixes the version:
  `Hl7.Fhir.R4`. Never reference two versions' packages in one service: their models differ.
- **SDK 6:** `FhirJsonParser` is obsolete (and fails a warnings-as-errors build). Read JSON with
  `FhirJsonDeserializer`; write it with `resource.ToJson()`.

## FHIR Messaging: what exchange platforms use

National exchange platforms (eligibility, claims, referrals) mostly use **messaging**, not the REST
API: one `Bundle` of type `message`, its `MessageHeader` first, posted to one endpoint with
organisation-level certificates, and checked against the platform's own profiles.

<!-- sample: tests/SkillSamples.Tests/Fhir/FhirMessages.cs -->
```csharp
// FHIR Messaging, as national exchange platforms use it: one Bundle of type "message", its MessageHeader
// first, posted to one endpoint. Everything the header points at travels in the same bundle.
public static class FhirMessages
{
    public static Bundle Request(string eventCode, Uri source, Uri destination, Resource focus, DateTimeOffset now)
    {
        var focusUrl = $"urn:uuid:{Guid.NewGuid()}";
        var header = new MessageHeader
        {
            Id = Guid.NewGuid().ToString(),
            Event = new Coding("http://example.org/fhir/message-events", eventCode),
            Source = new MessageHeader.MessageSourceComponent { Endpoint = source.ToString() },
            Destination = [new MessageHeader.MessageDestinationComponent { Endpoint = destination.ToString() }],
            Focus = [new ResourceReference(focusUrl)]
        };

        return new Bundle
        {
            Type = Bundle.BundleType.Message,
            Identifier = new Identifier("urn:ietf:rfc:3986", $"urn:uuid:{Guid.NewGuid()}"),
            Timestamp = now,
            Entry =
            [
                new Bundle.EntryComponent { FullUrl = $"urn:uuid:{header.Id}", Resource = header },   // the header is first
                new Bundle.EntryComponent { FullUrl = focusUrl, Resource = focus }
            ]
        };
    }

    // R4: a response's MessageHeader.response.identifier is the id of the request's MessageHeader.
    public static bool IsResponseTo(Bundle response, Bundle request) =>
        Header(response).Response?.Identifier is { } answered && answered == Header(request).Id;

    public static MessageHeader Header(Bundle message) =>
        message.Entry.FirstOrDefault()?.Resource as MessageHeader
        ?? throw new InvalidOperationException("A FHIR message starts with its MessageHeader.");
}
```

Tested as stories (each message is written to JSON and read back first):
- **An eligibility request is built:** the bundle is a message, the header comes first, and its focus
  points at the patient's entry.
- **The response arrives:** it's matched to its request by the request header's id, not by the bundle's
  identifier. The old version of this skill said to match on `Bundle.identifier`. Check your platform's
  implementation guide: some add their own rules on top of R4's.

Validate against the platform's profiles (its implementation guide), not only base FHIR: base FHIR is
deliberately loose.

## Reading what arrives

`FhirJsonDeserializer.DEFAULT.Deserialize<T>(json)` refuses content the model doesn't know. Tested as a
story: a patient with an extra field fails with `DeserializationFailedException`, and its
`PartialResult` holds what could be read. Decide per integration: refuse (and log the issues), or use a
more lenient mode (`FhirJsonDeserializer.RECOVERABLE`) and record what was dropped. Don't use the most
lenient (`OSTRICH`) for clinical data.

## FHIRPath

A path language for reading resources without null-walking: `Select`, `Scalar`, `IsTrue` (namespace
`Hl7.Fhir.FhirPath`). Tested: `Patient.name.where(use = 'official').family` picks the official name from a
patient who has two.

Tested with no ICU and no tzdata: Arabic names and the bundle's `+03:00` timestamp survive the round trip.

## The REST API (`FhirClient`)

For servers that expose FHIR over REST: `new FhirClient(baseUrl)`, `ReadAsync<Patient>("Patient/123")`,
`SearchAsync<Observation>(["subject=Patient/123"])`, with `_include` and chained search to avoid round
trips. Not tested here (it needs a FHIR server); give it an `HttpClient` from `IHttpClientFactory`.

## Rules
- Firely SDK, `Hl7.Fhir.R4`, one FHIR version per service.
- `FhirJsonDeserializer`, with a deliberate choice of how strict.
- Messages: header first, everything it references in the bundle, responses matched by header id.
- Validate against the profile you must conform to.
- PHI rules apply to FHIR payloads as to everything else (`healthcare-compliance`): no payloads in logs.
