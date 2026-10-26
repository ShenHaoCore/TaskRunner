using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Common;

namespace TaskRunner.Api.Auth;

/// <summary>生产环境未配置管理员密钥时启动即失败（fail-fast），不允许鉴权裸奔。</summary>
public sealed class AdminKeyGuardStartupFilter(
    IOptions<TaskRunnerOptions> options,
    IHostEnvironment environment) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        if (!environment.IsDevelopment() && string.IsNullOrWhiteSpace(options.Value.AdminApiKey))
        {
            throw new InvalidOperationException("生产环境必须配置 Hangfire:AdminApiKey。");
        }

        next(app);
    };
}
