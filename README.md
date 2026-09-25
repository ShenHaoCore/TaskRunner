# TaskRunner

基于 Hangfire 的 .NET 9 任务调度。开发时 **Api 单进程**（入队 + 执行）；生产 Api 只入队/Dashboard，Worker 作为 Windows 服务执行。共享调度在 `TaskRunner.Hangfire`，Core 不引用 Hangfire。

## 运行（开发，LocalDB）

```powershell
./scripts/ensure-localdb.ps1
dotnet test
dotnet run --project src/TaskRunner.Api
```

开发环境 Api 角色为 `Combined`，**不必**再开 Worker。

- 首页：http://localhost:5088
- Dashboard：http://localhost:5088/taskrunner
- Scalar：http://localhost:5088/scalar
- B 站扫码：http://localhost:5088/bilibili-login.html
- 任务 API：http://localhost:5088/api/tasks

日志：`logs/taskrunner-*.log`（Api/Worker 共用文件，按日志中的 `Application` 列区分）。

## 鉴权与只读

| 环境 | 行为 |
|------|------|
| Development | Dashboard / 写操作免密钥 |
| Production | 配置 `Hangfire:AdminApiKey`；写 API 用头 `X-TaskRunner-Admin`，或 `/api/auth/login` 写 Cookie；Dashboard 可用 Basic（密码=ApiKey） |

只读由配置 `Hangfire:ReadOnlyMode` 控制（改配置需重启）。

## 新增任务

实现 `IRecurringJob` 并标记 `[RecurringTask]`；可选 `[ExclusiveExecution(秒)]`。扩展程序集启动时传给 `AddTaskRunnerCore(..., extraAssemblies)`。重启 Api 后自动注册。

```csharp
[RecurringTask("0 0 9 * * *", Description = "每日示例")]
[ExclusiveExecution(3600)]
public sealed class MyJob : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

Cron 支持 5/6 位。已存在任务的 Cron / 启用状态以 `TaskConfigs` 为准。

## B 站每日任务

实现在 `TaskRunner.Bilibili`，默认每天 09:00。扫码：打开 bilibili-login.html → 生成二维码 → 手机确认。开发写入仓库 `data/bilibili-cookie.txt`；发布无 `.sln` 时默认 `%LocalAppData%\TaskRunner\bilibili-cookie.txt`。也可设 `Bilibili:CookieFile` / `Bilibili:Cookie`。

Hangfire.Console（作业详情页底部）：Dashboard → Succeeded/Processing → 点进该次 `bilibili-daily` 作业详情。

## SQL Server / 生产

```powershell
./scripts/ensure-localdb.ps1   # 仅开发 LocalDB
```

只需一个数据库 `TaskRunner` 与一个连接字符串 `ConnectionStrings:Default`：Hangfire 表位于同库的 `HangFire` schema，业务表 `TaskConfigs` 由 EF 迁移在启动时自动创建。公共默认配置集中在 `src/appsettings.shared.json`（Api/Worker 构建时链接共享），生产用环境变量或各项目自己的 `appsettings.json` 覆盖，并设置 `Hangfire:AdminApiKey`。成功作业默认保留 60 分钟（`SucceededJobExpirationMinutes`）。

生产请单独运行 Worker（或安装 Windows 服务）：

```powershell
dotnet run --project src/TaskRunner.Worker
# 管理员
./scripts/install-worker-service.ps1
./scripts/uninstall-worker-service.ps1
```

## 项目结构

```
src/
  TaskRunner.Api/        # 开发 Combined；生产 Client + Dashboard + REST
  TaskRunner.Worker/     # 生产 Server（Windows 服务）
  TaskRunner.Hangfire/   # Hangfire 装配 + Console
  TaskRunner.Core/       # 任务契约与配置（无 Hangfire）
  TaskRunner.Bilibili/   # B 站每日任务
```