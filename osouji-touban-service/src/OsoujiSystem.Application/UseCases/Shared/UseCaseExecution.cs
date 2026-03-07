using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.Shared;

internal static class UseCaseExecution
{
    public static async Task<ApplicationResult<T>> InTransaction<T>(
        IApplicationTransaction transaction,
        Func<CancellationToken, Task<ApplicationResult<T>>> action,
        CancellationToken ct)
    {
        try
        {
            return await transaction.ExecuteAsync(action, ct);
        }
        catch (RepositoryConcurrencyException ex)
        {
            return ApplicationResult<T>.Failure(
                "RepositoryConcurrency",
                "The aggregate was updated by another transaction.",
                new Dictionary<string, object?> { ["detail"] = ex.Message });
        }
        catch (RepositoryDuplicateException ex)
        {
            return ApplicationResult<T>.Failure(
                "RepositoryDuplicate",
                "A duplicate aggregate was detected.",
                new Dictionary<string, object?> { ["detail"] = ex.Message });
        }
    }

    public static async Task DispatchAndClearAsync<TId>(
        IDomainEventDispatcher dispatcher,
        AggregateRoot<TId> aggregate,
        CancellationToken ct)
    {
        if (aggregate.DomainEvents.Count == 0)
        {
            return;
        }

        await dispatcher.DispatchAsync(aggregate.DomainEvents, ct);
        aggregate.ClearDomainEvents();
    }
}
