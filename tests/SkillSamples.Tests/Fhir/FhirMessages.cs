using Hl7.Fhir.Model;

namespace SkillSamples.Fhir;

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
