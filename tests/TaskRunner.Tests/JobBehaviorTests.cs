using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs;
using TaskRunner.Core.Services;

namespace TaskRunner.Tests;

public sealed class JobBehaviorTests
{
    [Fact]
    public async Task Executor_LogsSuccess()
    {
        var logger = new RecordingLogger<JobExecutor>();
        var job = new SuccessRecurringJob();
        await using var provider = BuildProvider(logger, services => services.AddSingleton(job));
        await using var scope = provider.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();

        await executor.ExecuteAsync("success", JobTypeName.For(typeof(SuccessRecurringJob)), CancellationToken.None);

        Assert.Equal(1, job.Count);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("执行成功", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Executor_LogsAndRethrowsFailures()
    {
        var logger = new RecordingLogger<JobExecutor>();
        await using var provider = BuildProvider(logger, services => services.AddSingleton<FailingRecurringJob>());
        await using var scope = provider.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("failing", JobTypeName.For(typeof(FailingRecurringJob)), CancellationToken.None));

        Assert.Equal("boom", exception.Message);
        // 失败日志只在 Gateway 层记 Error（带耗时与 Console 输出），Executor 不再重复记录
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Executor_MissingType_SkipsWithoutThrow()
    {
        var logger = new RecordingLogger<JobExecutor>();
        await using var provider = BuildProvider(logger, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();

        // 代码已删除但队列仍可能入队：直接出队（Warning），不抛异常避免重试刷屏
        var exception = await Record.ExceptionAsync(() =>
            executor.ExecuteAsync("ghost", "Deleted.Job, Gone", CancellationToken.None));

        Assert.Null(exception);
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("类型已不存在", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Executor_NonRecurringJobType_Throws()
    {
        var logger = new RecordingLogger<JobExecutor>();
        await using var provider = BuildProvider(logger, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();

        // 类型存在但没实现 IRecurringJob：配置漂移必须显式失败
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync("bad", JobTypeName.For(typeof(string)), CancellationToken.None));
    }

    private static ServiceProvider BuildProvider(RecordingLogger<JobExecutor> logger, Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<JobExecutor>>(logger);
        services.AddScoped<JobExecutor>();
        configure(services);
        return services.BuildServiceProvider();
    }
}

public sealed class SuccessRecurringJob : IRecurringJob
{
    public int Count { get; private set; }

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        Count++;
        return Task.CompletedTask;
    }
}

public sealed class FailingRecurringJob : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
