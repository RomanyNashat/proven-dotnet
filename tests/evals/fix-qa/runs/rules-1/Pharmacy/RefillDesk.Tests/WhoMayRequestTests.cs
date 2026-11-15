using FluentAssertions;
using static RefillDesk.Tests.TestPharmacy;

namespace RefillDesk.Tests;

/// <summary>AC1: the patient, or a caregiver with a delegation valid that day; anyone else is refused.</summary>
public sealed class WhoMayRequestTests
{
    private static readonly DateOnly Today = DefaultDay;

    [Fact]
    public void Request_PatientOwnPrescription_IsAccepted()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx());

        var result = pharmacy.Request(PatientId);

        result.Should().BeEquivalentTo(new { Ok = true, Refusal = RefillRefusal.None });
        result.Request!.PrescriptionId.Should().Be(1);
    }

    [Fact]
    public void Request_Stranger_IsRefusedAsNotTheirPrescription()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx());

        var result = pharmacy.Request(StrangerId);

        result.Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    [Fact]
    public void Request_AnotherPatient_IsRefusedAsNotTheirPrescription()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx(id: 1, patientId: PatientId));
        pharmacy.Add(Rx(id: 2, patientId: OtherPatientId));

        var result = pharmacy.Request(OtherPatientId, prescriptionId: 1);

        result.Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    [Theory]
    [InlineData(-30, 30)] // well inside
    [InlineData(0, 30)] // first day
    [InlineData(-30, 0)] // last day
    [InlineData(0, 0)] // one-day delegation
    public void Request_CaregiverWithDelegationValidToday_IsAccepted(int fromOffset, int toOffset)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx());
        pharmacy.Delegate(CaregiverId, PatientId, Today.AddDays(fromOffset), Today.AddDays(toOffset));

        var result = pharmacy.Request(CaregiverId);

        result.Refusal.Should().Be(RefillRefusal.None);
    }

    [Theory]
    [InlineData(1, 30)] // starts tomorrow
    [InlineData(-30, -1)] // ended yesterday
    public void Request_CaregiverWithDelegationNotValidToday_IsRefusedAsNotTheirPrescription(int fromOffset, int toOffset)
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx());
        pharmacy.Delegate(CaregiverId, PatientId, Today.AddDays(fromOffset), Today.AddDays(toOffset));

        var result = pharmacy.Request(CaregiverId);

        result.Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    [Fact]
    public void Request_CaregiverWithoutAnyDelegation_IsRefusedAsNotTheirPrescription()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx());

        var result = pharmacy.Request(CaregiverId);

        result.Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    [Fact]
    public void Request_CaregiverDelegatedForAnotherPatient_IsRefusedAsNotTheirPrescription()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx());
        pharmacy.Delegate(CaregiverId, OtherPatientId, Today.AddDays(-30), Today.AddDays(30));

        var result = pharmacy.Request(CaregiverId);

        result.Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    [Fact]
    public void Request_DelegationHeldByAnotherCaregiver_IsRefusedAsNotTheirPrescription()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx());
        pharmacy.Delegate(OtherCaregiverId, PatientId, Today.AddDays(-30), Today.AddDays(30));

        var result = pharmacy.Request(CaregiverId);

        result.Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    [Fact]
    public void Request_CaregiverWithExpiredAndCurrentDelegations_IsAccepted()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx());
        pharmacy.Delegate(CaregiverId, PatientId, Today.AddDays(-60), Today.AddDays(-31));
        pharmacy.Delegate(CaregiverId, PatientId, Today.AddDays(-5), Today.AddDays(5));

        var result = pharmacy.Request(CaregiverId);

        result.Refusal.Should().Be(RefillRefusal.None);
    }

    [Fact]
    public void Request_CaregiverWithCurrentDelegationForOtherPatientAndLapsedOneForThisPatient_IsRefused()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx());
        pharmacy.Delegate(CaregiverId, PatientId, Today.AddDays(-60), Today.AddDays(-1));
        pharmacy.Delegate(CaregiverId, OtherPatientId, Today.AddDays(-5), Today.AddDays(5));

        var result = pharmacy.Request(CaregiverId);

        result.Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    [Fact]
    public void Request_Refused_CreatesNoRequest()
    {
        var pharmacy = new TestPharmacy();
        pharmacy.Add(Rx());

        var result = pharmacy.Request(StrangerId);

        result.Request.Should().BeNull();
        pharmacy.Memory.RequestsFor(1).Should().BeEmpty();
    }

    [Fact]
    public void Request_UnknownPrescription_IsRefused()
    {
        var pharmacy = new TestPharmacy();

        var result = pharmacy.Request(PatientId, prescriptionId: 404);

        result.Ok.Should().BeFalse();
    }
}
