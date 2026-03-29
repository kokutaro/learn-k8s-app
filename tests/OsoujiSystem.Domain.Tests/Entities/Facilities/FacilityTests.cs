using AwesomeAssertions;
using OsoujiSystem.Domain.Entities.Facilities;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Events;

namespace OsoujiSystem.Domain.Tests.Entities.Facilities;

public sealed class FacilityTests
{
    [Fact]
    public void Register_WhenInputIsValid_ShouldSucceedAndPublishRegisteredEvent()
    {
        var result = Facility.Register(
            FacilityId.New(),
            FacilityCode.Create("tokyo-hq").Value,
            FacilityName.Create("Tokyo HQ").Value,
            "Main office",
            FacilityTimeZone.Create("Asia/Tokyo").Value);

        result.IsSuccess.Should().BeTrue();
        result.Value.Code.Value.Should().Be("TOKYO-HQ");
        result.Value.LifecycleStatus.Should().Be(FacilityLifecycleStatus.Active);
        result.Value.DomainEvents.Should().ContainSingle(e => e is FacilityRegistered);
    }

    [Fact]
    public void UpdateProfile_WhenValuesChange_ShouldPublishUpdatedEvent()
    {
        var facility = CreateFacility();
        facility.ClearDomainEvents();

        var result = facility.UpdateProfile(
            FacilityName.Create("Tokyo HQ Annex").Value,
            "Annex building",
            FacilityTimeZone.Create("Asia/Tokyo").Value);

        result.IsSuccess.Should().BeTrue();
        facility.DomainEvents.Should().ContainSingle();
        var updated = facility.DomainEvents.Single().Should().BeOfType<FacilityUpdated>().Subject;
        updated.ChangeType.Should().Be(FacilityChangeType.ProfileUpdated);
        updated.ChangedFields.Should().Contain("name");
    }

    [Fact]
    public void ChangeLifecycle_WhenStatusChanges_ShouldPublishUpdatedEvent()
    {
        var facility = CreateFacility();
        facility.ClearDomainEvents();

        var result = facility.ChangeLifecycle(FacilityLifecycleStatus.Inactive);

        result.IsSuccess.Should().BeTrue();
        facility.LifecycleStatus.Should().Be(FacilityLifecycleStatus.Inactive);
        var updated = facility.DomainEvents.Single().Should().BeOfType<FacilityUpdated>().Subject;
        updated.ChangeType.Should().Be(FacilityChangeType.LifecycleChanged);
    }

    [Fact]
    public void FacilityCode_Create_WhenInvalid_ShouldFail()
    {
        var result = FacilityCode.Create(" ");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidFacilityCodeError>();
    }

    private static Facility CreateFacility()
        => Facility.Register(
            FacilityId.New(),
            FacilityCode.Create("TOKYO-HQ").Value,
            FacilityName.Create("Tokyo HQ").Value,
            "Main office",
            FacilityTimeZone.Create("Asia/Tokyo").Value).Value;
}
