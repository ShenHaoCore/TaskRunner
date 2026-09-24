using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Api.Auth;
using TaskRunner.Core.Models;
using TaskRunner.Core.Services;

namespace TaskRunner.Api.Controllers;

[ApiController]
[Route("api/background-jobs")]
[Tags("后台任务")]
public sealed class BackgroundJobsController(IJobCatalog catalog, ITaskScheduler scheduler) : ControllerBase
{
    [HttpGet(Name = "ListBackgroundJobs")]
    [EndpointSummary("列表")]
    public ActionResult<IReadOnlyList<BackgroundJobView>> List()
        => Ok(catalog.BackgroundJobs
            .Select(job => new BackgroundJobView(job.JobId, job.JobName, job.JobType, job.Description))
            .ToList());

    [Authorize(Policy = AdminAuthDefaults.Policy)]
    [HttpPost("{jobId}/trigger", Name = "TriggerBackgroundJob")]
    [EndpointSummary("触发")]
    public ActionResult<QueueJobResult> Trigger(string jobId)
    {
        var job = catalog.BackgroundJobs.FirstOrDefault(item => string.Equals(item.JobId, jobId, StringComparison.Ordinal));
        if (job is null)
        {
            return NotFound(new { message = $"后台任务不存在：{jobId}" });
        }

        var queueJobId = scheduler.EnqueueBackground(job.JobId, job.JobType);
        return Ok(new QueueJobResult(job.JobId, queueJobId));
    }
}
