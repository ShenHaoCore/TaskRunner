using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskRunner.Api.Services;
using TaskRunner.Core.Common;
using TaskRunner.Core.Models;

namespace TaskRunner.Api.Controllers;

/// <summary>首页（仪表盘入口）：匿名可见骨架，登录后填充实时统计。</summary>
[Route("")]
public sealed class HomeController(
    HangfireMonitoringReader monitoring,
    IOptions<TaskRunnerOptions> options,
    IHostEnvironment environment) : Controller
{
    [HttpGet("/")]
    public IActionResult Index()
    {
        ViewData["Title"] = "TaskRunner — 任务调度平台";
        ViewData["NavActive"] = "home";
        ViewData["AutoRefresh"] = "30";

        var authenticated = User.Identity?.IsAuthenticated == true;
        var stats = authenticated ? monitoring.ReadStatistics() : null;
        var heartbeat = authenticated && options.Value.HeartbeatEnabled
            ? monitoring.ReadHeartbeat()
            : null;
        var recentFailures = authenticated
            ? monitoring.ReadRecentExecutions(limit: 5, failuresOnly: true)
            : [];

        return View(new HomeViewModel(
            Authenticated: authenticated,
            DashboardPath: options.Value.NormalizeDashboardPath(),
            HeartbeatEnabled: options.Value.HeartbeatEnabled,
            Environment: environment.EnvironmentName,
            ReadOnly: options.Value.ReadOnlyMode,
            Stats: stats,
            WorkerOnline: (stats?.Servers ?? 0) > 0,
            HeartbeatLast: heartbeat?.LastExecution,
            HeartbeatNext: heartbeat?.NextExecution,
            RecentFailures: recentFailures));
    }

    public sealed record HomeViewModel(
        bool Authenticated,
        string DashboardPath,
        bool HeartbeatEnabled,
        string Environment,
        bool ReadOnly,
        JobStatistics? Stats,
        bool WorkerOnline,
        DateTime? HeartbeatLast,
        DateTime? HeartbeatNext,
        IReadOnlyList<RecentExecution> RecentFailures);
}
