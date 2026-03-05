using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.Dispatching;
using OsoujiSystem.Application.Time;
using OsoujiSystem.Application.UseCases.WeeklyDutyPlans;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.EventHandlers;

public sealed class RebalanceOnUserAssignedHandler : INotificationHandler<DomainEventNotification>
{
    private readonly ICleaningAreaRepository _cleaningAreaRepository;
    private readonly IWeeklyDutyPlanRepository _weeklyDutyPlanRepository;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public RebalanceOnUserAssignedHandler(
        ICleaningAreaRepository cleaningAreaRepository,
        IWeeklyDutyPlanRepository weeklyDutyPlanRepository,
        IMediator mediator,
        IClock clock)
    {
        _cleaningAreaRepository = cleaningAreaRepository;
        _weeklyDutyPlanRepository = weeklyDutyPlanRepository;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task Handle(DomainEventNotification notification, CancellationToken ct)
    {
        if (notification.DomainEvent is not UserAssignedToArea ev)
        {
            return;
        }

        var areaLoaded = await _cleaningAreaRepository.FindByIdAsync(ev.AreaId, ct);
        if (areaLoaded is null)
        {
            return;
        }

        var weekId = WeekContextResolver.ResolveCurrentWeek(_clock, areaLoaded.Value.Aggregate.CurrentWeekRule);
        var planLoaded = await _weeklyDutyPlanRepository.FindByAreaAndWeekAsync(ev.AreaId, weekId, ct);
        if (planLoaded is null)
        {
            return;
        }

        await _mediator.Send(new RebalanceForUserAssignedRequest
        {
            PlanId = planLoaded.Value.Aggregate.Id,
            AddedUserId = ev.UserId
        }, ct);
    }
}

public sealed class RebalanceOnUserUnassignedHandler : INotificationHandler<DomainEventNotification>
{
    private readonly ICleaningAreaRepository _cleaningAreaRepository;
    private readonly IWeeklyDutyPlanRepository _weeklyDutyPlanRepository;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public RebalanceOnUserUnassignedHandler(
        ICleaningAreaRepository cleaningAreaRepository,
        IWeeklyDutyPlanRepository weeklyDutyPlanRepository,
        IMediator mediator,
        IClock clock)
    {
        _cleaningAreaRepository = cleaningAreaRepository;
        _weeklyDutyPlanRepository = weeklyDutyPlanRepository;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task Handle(DomainEventNotification notification, CancellationToken ct)
    {
        if (notification.DomainEvent is not UserUnassignedFromArea ev)
        {
            return;
        }

        var areaLoaded = await _cleaningAreaRepository.FindByIdAsync(ev.AreaId, ct);
        if (areaLoaded is null)
        {
            return;
        }

        var weekId = WeekContextResolver.ResolveCurrentWeek(_clock, areaLoaded.Value.Aggregate.CurrentWeekRule);
        var planLoaded = await _weeklyDutyPlanRepository.FindByAreaAndWeekAsync(ev.AreaId, weekId, ct);
        if (planLoaded is null)
        {
            return;
        }

        await _mediator.Send(new RebalanceForUserUnassignedRequest
        {
            PlanId = planLoaded.Value.Aggregate.Id,
            RemovedUserId = ev.UserId
        }, ct);
    }
}

public sealed class RecalculateOnSpotChangedHandler : INotificationHandler<DomainEventNotification>
{
    private readonly ICleaningAreaRepository _cleaningAreaRepository;
    private readonly IWeeklyDutyPlanRepository _weeklyDutyPlanRepository;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public RecalculateOnSpotChangedHandler(
        ICleaningAreaRepository cleaningAreaRepository,
        IWeeklyDutyPlanRepository weeklyDutyPlanRepository,
        IMediator mediator,
        IClock clock)
    {
        _cleaningAreaRepository = cleaningAreaRepository;
        _weeklyDutyPlanRepository = weeklyDutyPlanRepository;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task Handle(DomainEventNotification notification, CancellationToken ct)
    {
        if (notification.DomainEvent is not CleaningSpotAdded and not CleaningSpotRemoved)
        {
            return;
        }

        var areaId = notification.DomainEvent switch
        {
            CleaningSpotAdded added => added.AreaId,
            CleaningSpotRemoved removed => removed.AreaId,
            _ => default
        };

        if (areaId == default)
        {
            return;
        }

        var areaLoaded = await _cleaningAreaRepository.FindByIdAsync(areaId, ct);
        if (areaLoaded is null)
        {
            return;
        }

        var weekId = WeekContextResolver.ResolveCurrentWeek(_clock, areaLoaded.Value.Aggregate.CurrentWeekRule);
        var planLoaded = await _weeklyDutyPlanRepository.FindByAreaAndWeekAsync(areaId, weekId, ct);
        if (planLoaded is null)
        {
            return;
        }

        await _mediator.Send(new RecalculateForSpotChangedRequest
        {
            PlanId = planLoaded.Value.Aggregate.Id
        }, ct);
    }
}
