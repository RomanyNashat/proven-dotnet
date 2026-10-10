using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Fhir;

public sealed class FhirTests
{
    private static readonly Uri Us = new("https://hospital.example.org/fhir");
    private static readonly Uri Platform = new("https://exchange.example.org/fhir/$process-message");
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.FromHours(3));

    private static Patient Sara() => new()
    {
        Id = "p1",
        Name =
        [
            new HumanName { Use = HumanName.NameUse.Usual, Given = ["Sara"], Family = "Ahmed" },
            new HumanName { Use = HumanName.NameUse.Official, Given = ["Sara"], Family = "Al-Harbi" }
        ],
        BirthDate = "1990-05-01"
    };

    // What goes over the wire and what comes back: JSON, read with the SDK's deserializer.
    private static T RoundTrip<T>(T resource) where T : Resource =>
        FhirJsonDeserializer.DEFAULT.Deserialize<T>(resource.ToJson());

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_AnEligibilityRequestIsBuilt_TheHeaderComesFirstAndPointsAtThePatient()
    {
        var message = RoundTrip(FhirMessages.Request("eligibility-request", Us, Platform, Sara(), Now));

        Assert.True(message.IsTrue("Bundle.type = 'message'"));
        Assert.True(message.IsTrue("Bundle.entry.first().resource is MessageHeader"));
        Assert.Equal(
            message.Scalar("Bundle.entry.resource.ofType(MessageHeader).focus.reference"),
            message.Scalar("Bundle.entry.where(resource is Patient).fullUrl"));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_TheResponseArrives_ItIsMatchedToItsRequestByTheHeaderId()
    {
        var request = FhirMessages.Request("eligibility-request", Us, Platform, Sara(), Now);
        var other = FhirMessages.Request("eligibility-request", Us, Platform, Sara(), Now);
        var answer = new CoverageEligibilityResponse
        {
            Status = FinancialResourceStatusCodes.Active,
            Purpose = [CoverageEligibilityResponse.EligibilityResponsePurpose.Validation],
            Patient = new ResourceReference("Patient/p1"),
            Created = "2026-10-11",
            Request = new ResourceReference("CoverageEligibilityRequest/r1"),
            Outcome = ClaimProcessingCodes.Complete,
            Insurer = new ResourceReference("Organization/insurer-1")
        };
        var response = FhirMessages.Request("eligibility-response", Platform, Us, answer, Now);
        FhirMessages.Header(response).Response = new MessageHeader.ResponseComponent
        {
            Identifier = FhirMessages.Header(request).Id,
            Code = MessageHeader.ResponseType.Ok
        };

        response = RoundTrip(response);

        Assert.True(FhirMessages.IsResponseTo(response, request));
        Assert.False(FhirMessages.IsResponseTo(response, other));
        Assert.NotEqual(request.Identifier!.Value, FhirMessages.Header(request).Id);   // not the bundle's identifier
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_ThePlatformAddsAFieldTheModelDoesntKnow_TheDefaultDeserializerRefusesIt()
    {
        const string json = """{"resourceType":"Patient","id":"p1","favouriteColour":"blue"}""";

        var refused = Assert.Throws<DeserializationFailedException>(() => FhirJsonDeserializer.DEFAULT.Deserialize<Patient>(json));

        Assert.Equal("p1", Assert.IsType<Patient>(refused.PartialResult).Id);   // what could be read is still there
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_APatientHasTwoNames_FhirPathPicksTheOfficialOne()
    {
        Assert.Equal("Al-Harbi", Sara().Scalar("Patient.name.where(use = 'official').family"));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public void Production_NoIcuNoTzdata_ArabicNamesAndTheRiyadhOffsetSurvive()
    {
        ProductionConditions.Require();
        var patient = new Patient { Id = "p2", Name = [new HumanName { Text = "سارة أحمد الحربي" }] };

        var message = RoundTrip(FhirMessages.Request("eligibility-request", Us, Platform, patient, Now));

        Assert.Equal("سارة أحمد الحربي", message.Scalar("Bundle.entry.resource.ofType(Patient).name.text"));
        Assert.Equal(Now, message.Timestamp);
        Assert.Equal(TimeSpan.FromHours(3), message.Timestamp!.Value.Offset);
    }
}
