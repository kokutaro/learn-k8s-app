using AwesomeAssertions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.Notifications;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.Facilities;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;
using OsoujiSystem.Infrastructure.Notifications;
using OsoujiSystem.Infrastructure.Serialization;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class NotificationDesignTests
{
    [Fact]
    public async Task WeeklyPlanNotificationFactory_ShouldBuildCurrentWeekNotifications_ForPublishedPlan()
    {
        var weekId = WeekId.FromDate(new DateOnly(2026, 3, 2));
        var area = CreateArea(weekId);
        var sinkSpot = area.Spots[0];
        var hallSpot = area.Spots[1];
        var assignedUserId = UserId.New();
        var offDutyUserId = UserId.New();
        var plan = CreatePublishedPlan(area.Id, weekId, [new DutyAssignment(sinkSpot.Id, assignedUserId)], [new OffDutyEntry(offDutyUserId)]);
        var loggerFactory = LoggerFactory.Create(_ => { });
        var jsonSerializer = new InfrastructureJsonSerializer();
        var factory = new WeeklyPlanNotificationFactory(
            new FakeWeeklyDutyPlanRepository(plan),
            new FakeCleaningAreaRepository(area),
            new FixedClock(new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero)),
            loggerFactory.CreateLogger<WeeklyPlanNotificationFactory>(),
            jsonSerializer);

        var notifications = await factory.BuildAsync(
            "weekly-plan.published",
            Json(new WeeklyPlanPublished(plan.Id, weekId, plan.Revision)),
            new Dictionary<string, object?>
            {
                ["event_id"] = Guid.Parse("11111111-1111-1111-1111-111111111111").ToString("D")
            },
            CancellationToken.None);

        notifications.Should().HaveCount(2);
        notifications.Should().ContainSingle(x =>
            x.RecipientUserId == assignedUserId.Value
            && x.NotificationType == "weekly-duty-plan.assignment.confirmed"
            && x.Body.Contains("Sink"));
        notifications.Should().ContainSingle(x =>
            x.RecipientUserId == offDutyUserId.Value
            && x.NotificationType == "weekly-duty-plan.off-duty.confirmed"
            && x.Body.Contains("担当なし"));
        notifications.Should().OnlyContain(x => x.Metadata["weekId"] == weekId.ToString());
        notifications.Should().NotContain(x => x.Body.Contains(hallSpot.Name));
    }

    [Fact]
    public async Task WeeklyPlanNotificationFactory_ShouldSkipPlan_WhenWeekIsNotCurrent()
    {
        var planWeek = WeekId.FromDate(new DateOnly(2026, 3, 9));
        var currentWeek = WeekId.FromDate(new DateOnly(2026, 3, 2));
        var area = CreateArea(currentWeek);
        var userId = UserId.New();
        var plan = CreatePublishedPlan(area.Id, planWeek, [new DutyAssignment(area.Spots[0].Id, userId)], []);
        var loggerFactory = LoggerFactory.Create(_ => { });
        var jsonSerializer = new InfrastructureJsonSerializer();
        var factory = new WeeklyPlanNotificationFactory(
            new FakeWeeklyDutyPlanRepository(plan),
            new FakeCleaningAreaRepository(area),
            new FixedClock(new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero)),
            loggerFactory.CreateLogger<WeeklyPlanNotificationFactory>(),
            jsonSerializer);

        var notifications = await factory.BuildAsync(
            "weekly-plan.published",
            Json(new WeeklyPlanPublished(plan.Id, planWeek, plan.Revision)),
            new Dictionary<string, object?>
            {
                ["event_id"] = Guid.NewGuid().ToString("D")
            },
            CancellationToken.None);

        notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task NotificationDispatcher_ShouldSkipAlreadyDeliveredChannel_OnRetry()
    {
        var notification = new UserNotification(
            "evt:user:assigned",
            "weekly-duty-plan.assignment.changed",
            Guid.NewGuid(),
            "title",
            "body",
            new Dictionary<string, string>());
        var delivered = new HashSet<(string ChannelName, string NotificationId)>
        {
            ("email", notification.NotificationId)
        };
        var repository = new FakeNotificationDeliveryLogRepository(delivered);
        var emailChannel = new FakeNotificationChannel("email");
        var slackChannel = new FakeNotificationChannel("slack");
        var loggerFactory = LoggerFactory.Create(_ => { });
        var dispatcher = new NotificationDispatcher(
            [emailChannel, slackChannel],
            repository,
            loggerFactory.CreateLogger<NotificationDispatcher>());

        await dispatcher.DispatchAsync([notification], CancellationToken.None);

        emailChannel.Sent.Should().BeEmpty();
        slackChannel.Sent.Should().ContainSingle().Which.NotificationId.Should().Be(notification.NotificationId);
        delivered.Should().Contain(("slack", notification.NotificationId));
    }

    private static CleaningArea CreateArea(WeekId weekId)
    {
        var weekRule = WeekRule.Create(DayOfWeek.Monday, new TimeOnly(0, 0), TimeZoneInfo.Utc.Id, weekId).Value;
        var registerResult = CleaningArea.Register(
            CleaningAreaId.New(),
            FacilityId.New(),
            "Ops",
            weekRule,
            [new CleaningSpot(CleaningSpotId.New(), "Sink", 10), new CleaningSpot(CleaningSpotId.New(), "Hall", 20)]);

        return registerResult.Value;
    }

    private static WeeklyDutyPlan CreatePublishedPlan(
        CleaningAreaId areaId,
        WeekId weekId,
        IReadOnlyList<DutyAssignment> assignments,
        IReadOnlyList<OffDutyEntry> offDutyEntries)
    {
        var generateResult = WeeklyDutyPlan.Generate(
            WeeklyDutyPlanId.New(),
            areaId,
            weekId,
            AssignmentPolicy.Default,
            assignments,
            offDutyEntries);
        var plan = generateResult.Value;
        plan.Publish().IsSuccess.Should().BeTrue();
        return plan;
    }

    private static ReadOnlyMemory<byte> Json<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeCleaningAreaRepository(CleaningArea area) : ICleaningAreaRepository
    {
        public Task<LoadedAggregate<CleaningArea>?> FindByIdAsync(CleaningAreaId areaId, CancellationToken ct)
            => Task.FromResult<LoadedAggregate<CleaningArea>?>(area.Id == areaId ? new LoadedAggregate<CleaningArea>(area, AggregateVersion.Initial) : null);

        public Task<LoadedAggregate<CleaningArea>?> FindByUserIdAsync(UserId userId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<LoadedAggregate<CleaningArea>>> ListAllAsync(CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<LoadedAggregate<CleaningArea>>> ListWeekRuleDueAsync(WeekId currentWeek, CancellationToken ct)
            => throw new NotSupportedException();

        public Task AddAsync(CleaningArea aggregate, CancellationToken ct)
            => throw new NotSupportedException();

        public Task SaveAsync(CleaningArea aggregate, AggregateVersion expectedVersion, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeWeeklyDutyPlanRepository(WeeklyDutyPlan plan) : IWeeklyDutyPlanRepository
    {
        public Task<LoadedAggregate<WeeklyDutyPlan>?> FindByIdAsync(WeeklyDutyPlanId planId, CancellationToken ct)
            => Task.FromResult<LoadedAggregate<WeeklyDutyPlan>?>(plan.Id == planId ? new LoadedAggregate<WeeklyDutyPlan>(plan, AggregateVersion.Initial) : null);

        public Task<LoadedAggregate<WeeklyDutyPlan>?> FindByAreaAndWeekAsync(CleaningAreaId areaId, WeekId weekId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<LoadedAggregate<WeeklyDutyPlan>>> ListAsync(CleaningAreaId? areaId, WeekId? weekId, WeeklyPlanStatus? status, CancellationToken ct)
            => throw new NotSupportedException();

        public Task AddAsync(WeeklyDutyPlan aggregate, CancellationToken ct)
            => throw new NotSupportedException();

        public Task SaveAsync(WeeklyDutyPlan aggregate, AggregateVersion expectedVersion, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeNotificationDeliveryLogRepository(HashSet<(string ChannelName, string NotificationId)> delivered)
        : INotificationDeliveryLogRepository
    {
        public Task<bool> HasSucceededAsync(string channelName, string notificationId, CancellationToken ct)
            => Task.FromResult(delivered.Contains((channelName, notificationId)));

        public Task MarkSucceededAsync(string channelName, UserNotification notification, CancellationToken ct)
        {
            delivered.Add((channelName, notification.NotificationId));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotificationChannel(string channelName) : INotificationChannel
    {
        public string ChannelName { get; } = channelName;

        public List<UserNotification> Sent { get; } = [];

        public Task SendAsync(UserNotification notification, CancellationToken ct)
        {
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }
}
