using Cortex.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.Behaviors;
using OsoujiSystem.Application.Dispatching;
using OsoujiSystem.Application.Observability;
using OsoujiSystem.Application.UseCases.WeeklyDutyPlans;
using OsoujiSystem.Domain.DomainServices;

namespace OsoujiSystem.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOsoujiApplication(this IServiceCollection services)
    {
        services.AddCortexMediator(
            [typeof(ServiceCollectionExtensions)],
            options =>
            {
                options.AddOpenCommandPipelineBehavior(typeof(TracingCommandBehavior<>));
                options.AddOpenCommandPipelineBehavior(typeof(TracingCommandBehavior<,>));
                options.AddOpenCommandPipelineBehavior(typeof(ApplicationErrorCommandPipelineBehavior<,>));
                options.AddOpenNotificationPipelineBehavior(typeof(ApplicationErrorNotificationPipelineBehavior<>));
                options.AddOpenNotificationPipelineBehavior(typeof(TracingNotificationBehavior<>));
            });

        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, DefaultIdGenerator>();
        services.TryAddScoped<IApplicationTransaction, NoopApplicationTransaction>();
        services.TryAddScoped<IDomainEventDispatcher, MediatRDomainEventDispatcher>();

        services.TryAddSingleton<FairnessPolicy>();
        services.TryAddScoped<DutyAssignmentEngine>();
        services.TryAddScoped<PlanComputationService>();

        return services;
    }
}
