using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OsoujiSystem.Infrastructure.Options;
using OsoujiSystem.Infrastructure.Projection;
using OsoujiSystem.Infrastructure.Queries.Caching;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class ReadModelCacheInvalidationRecoveryWorkerTests
{
    [Fact]
    public async Task ProcessAsync_ShouldResolveRemoveTask_AndAdvanceCheckpoint()
    {
        var task = new ReadModelCacheInvalidationTask(
            Guid.NewGuid(),
            MainProjector.ProjectorName,
            "readmodel:facility:123:latest",
            ReadModelCacheInvalidationOperationKind.Remove,
            10,
            0);
        var taskRepository = new FakeTaskRepository();
        var cache = new FakeReadModelCache();
        var advancer = new FakeCheckpointAdvancer();
        var worker = CreateWorker(taskRepository, cache, advancer);

        await worker.ProcessAsync(task, TestContext.Current.CancellationToken);

        taskRepository.ResolvedTaskIds.Should().ContainSingle().Which.Should().Be(task.TaskId);
        cache.RemovedKeys.Should().Contain(task.CacheKey);
        advancer.AdvancedProjectorNames.Should().ContainSingle().Which.Should().Be(MainProjector.ProjectorName);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRetryNamespaceIncrementTask()
    {
        var task = new ReadModelCacheInvalidationTask(
            Guid.NewGuid(),
            MainProjector.ProjectorName,
            "readmodel:ns:cleaning-areas:list",
            ReadModelCacheInvalidationOperationKind.IncrementNamespace,
            20,
            0);
        var taskRepository = new FakeTaskRepository();
        var cache = new FakeReadModelCache();
        var advancer = new FakeCheckpointAdvancer();
        var worker = CreateWorker(taskRepository, cache, advancer);

        await worker.ProcessAsync(task, TestContext.Current.CancellationToken);

        cache.IncrementedNamespaceKeys.Should().ContainSingle().Which.Should().Be(task.CacheKey);
        taskRepository.ResolvedTaskIds.Should().ContainSingle().Which.Should().Be(task.TaskId);
    }

    [Fact]
    public async Task ProcessAsync_ShouldMarkFailed_WhenCacheOperationThrows()
    {
        var task = new ReadModelCacheInvalidationTask(
            Guid.NewGuid(),
            MainProjector.ProjectorName,
            "readmodel:weekly-plan:123:latest",
            ReadModelCacheInvalidationOperationKind.Remove,
            30,
            2);
        var taskRepository = new FakeTaskRepository();
        var cache = new FakeReadModelCache { ThrowOnRemove = true };
        var advancer = new FakeCheckpointAdvancer();
        var worker = CreateWorker(taskRepository, cache, advancer);

        await worker.ProcessAsync(task, TestContext.Current.CancellationToken);

        taskRepository.FailedTaskIds.Should().ContainSingle().Which.Should().Be(task.TaskId);
        advancer.AdvancedProjectorNames.Should().BeEmpty();
    }

    private static ReadModelCacheInvalidationRecoveryWorker CreateWorker(
        FakeTaskRepository taskRepository,
        FakeReadModelCache cache,
        FakeCheckpointAdvancer advancer)
        => new(
            taskRepository,
            cache,
            advancer,
            Microsoft.Extensions.Options.Options.Create(new InfrastructureOptions()),
            NullLogger<ReadModelCacheInvalidationRecoveryWorker>.Instance);

    private sealed class FakeTaskRepository : IReadModelCacheInvalidationTaskRepository
    {
        public List<Guid> ResolvedTaskIds { get; } = [];
        public List<Guid> FailedTaskIds { get; } = [];

        public Task EnqueueAsync(
            string projectorName,
            string cacheKey,
            ReadModelCacheInvalidationOperationKind operationKind,
            long reasonGlobalPosition,
            string? lastError,
            CancellationToken ct)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ReadModelCacheInvalidationTask>> ListDueAsync(int batchSize, DateTimeOffset utcNow, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ReadModelCacheInvalidationTask>>([]);

        public Task MarkResolvedAsync(Guid taskId, CancellationToken ct)
        {
            ResolvedTaskIds.Add(taskId);
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(Guid taskId, int nextRetryCount, DateTimeOffset nextRetryAt, string lastError, CancellationToken ct)
        {
            FailedTaskIds.Add(taskId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeReadModelCache : IReadModelCache
    {
        public bool ThrowOnRemove { get; init; }
        public List<string> RemovedKeys { get; } = [];
        public List<string> IncrementedNamespaceKeys { get; } = [];

        public Task<T?> TryGetAsync<T>(string key, CancellationToken ct) => Task.FromResult(default(T));
        public Task<string?> TryGetStringAsync(string key, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct) => Task.CompletedTask;
        public Task SetStringAsync(string key, string value, TimeSpan ttl, CancellationToken ct) => Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct)
        {
            if (ThrowOnRemove)
            {
                throw new InvalidOperationException("boom");
            }

            RemovedKeys.Add(key);
            return Task.CompletedTask;
        }

        public Task<long> GetNamespaceVersionAsync(string key, CancellationToken ct) => Task.FromResult(0L);

        public Task<long> IncrementNamespaceVersionAsync(string key, TimeSpan ttl, CancellationToken ct)
        {
            IncrementedNamespaceKeys.Add(key);
            return Task.FromResult(1L);
        }
    }

    private sealed class FakeCheckpointAdvancer : IReadModelVisibilityCheckpointAdvancer
    {
        public List<string> AdvancedProjectorNames { get; } = [];

        public Task<ReadModelVisibilityAdvanceResult> AdvanceAsync(string projectorName, CancellationToken ct)
        {
            AdvancedProjectorNames.Add(projectorName);
            return Task.FromResult(new ReadModelVisibilityAdvanceResult(projectorName, 0, 0, 0, null, false));
        }
    }
}
