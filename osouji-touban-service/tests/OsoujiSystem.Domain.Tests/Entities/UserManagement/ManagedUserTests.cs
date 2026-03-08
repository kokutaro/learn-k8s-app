using AwesomeAssertions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.UserManagement;
using OsoujiSystem.Domain.Entities.UserManagement.ValueObjects;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Tests.Entities.UserManagement;

public sealed class ManagedUserTests
{
    [Fact]
    public void Register_WhenInputIsValid_ShouldSucceedAndPublishUserRegistered()
    {
        var result = ManagedUser.Register(
            UserId.New(),
            EmployeeNumber.Create("123456").Value,
            ManagedUserDisplayName.Create("Hanako").Value,
            ManagedUserEmailAddress.Create("hanako@example.com").Value,
            "OPS",
            RegistrationSource.AdminPortal);

        result.IsSuccess.Should().BeTrue();
        result.Value.LifecycleStatus.Should().Be(ManagedUserLifecycleStatus.Active);
        result.Value.DomainEvents.Should().ContainSingle(x => x is UserRegistered);
    }

    [Fact]
    public void UpdateProfile_WhenValuesChange_ShouldPublishUserUpdated()
    {
        var user = CreateUser();
        user.ClearDomainEvents();

        var result = user.UpdateProfile(
            ManagedUserDisplayName.Create("Taro Updated").Value,
            ManagedUserEmailAddress.Create("taro.updated@example.com").Value,
            "HR");

        result.IsSuccess.Should().BeTrue();
        user.DomainEvents.Should().ContainSingle();
        var updated = user.DomainEvents.Single().Should().BeOfType<UserUpdated>().Subject;
        updated.ChangeType.Should().Be(ManagedUserChangeType.ProfileUpdated);
        updated.ChangedFields.Should().Contain("displayName");
    }

    [Fact]
    public void ChangeLifecycle_WhenArchived_ShouldFailForFurtherUpdates()
    {
        var user = CreateUser();
        user.ClearDomainEvents();
        user.ChangeLifecycle(ManagedUserLifecycleStatus.Archived);
        user.ClearDomainEvents();

        var result = user.UpdateProfile(
            ManagedUserDisplayName.Create("Blocked").Value,
            null,
            "OPS");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ManagedUserAlreadyArchivedError>();
    }

    [Fact]
    public void LinkAuthIdentity_WhenNewLink_ShouldPublishUserUpdated()
    {
        var user = CreateUser();
        user.ClearDomainEvents();

        var result = user.LinkAuthIdentity(
            IdentityProviderKey.Create("entra-id").Value,
            IdentitySubject.Create("subject-123").Value,
            "taro@example.com");

        result.IsSuccess.Should().BeTrue();
        user.AuthIdentityLinks.Should().ContainSingle();
        user.DomainEvents.Should().ContainSingle();
        var updated = user.DomainEvents.Single().Should().BeOfType<UserUpdated>().Subject;
        updated.ChangeType.Should().Be(ManagedUserChangeType.AuthIdentityLinked);
    }

    [Fact]
    public void ManagedUserDisplayName_Create_WhenEmpty_ShouldFail()
    {
        var result = ManagedUserDisplayName.Create(" ");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidDisplayNameError>();
    }

    private static ManagedUser CreateUser()
        => ManagedUser.Register(
            UserId.New(),
            EmployeeNumber.Create("123456").Value,
            ManagedUserDisplayName.Create("Taro").Value,
            ManagedUserEmailAddress.Create("taro@example.com").Value,
            "OPS",
            RegistrationSource.AdminPortal).Value;
}
