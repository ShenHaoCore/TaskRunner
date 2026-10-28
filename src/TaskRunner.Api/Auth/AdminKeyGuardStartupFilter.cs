using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Common;

namespace TaskRunner.Api.Auth;

/// <summary>
/// 生产环境未配置管理员密钥时启动即失败（fail-fast）；未配置登录密码时记录警告
/// （Dashboard/REST 仍可用 Header/Basic，但 /login 表单不可用）。
/// </summary>
public sealed class AdminKeyGuardStartupFilter(
    IOptions<TaskRunnerOptions> options,
    IHostEnvironment environment) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        if (!environment.IsDevelopment())
        {
            if (string.IsNullOrWhiteSpace(options.Value.AdminApiKey))
            {
                throw new InvalidOperationException("生产环境必须配置 Hangfire:AdminApiKey。");
            }

            if (string.IsNullOrWhiteSpace(options.Value.AdminPassword))
            {
                app.ApplicationServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("TaskRunner.Auth")
                    .LogWarning(
                        "未配置 Hangfire:AdminPassword：/login 表单登录不可用。请在 /etc/default/taskrunner 补充账号密码后重启。");
            }
        }

        next(app);
    };
}
