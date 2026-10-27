# TaskRunner

[![.NET CICD](https://github.com/ShenHaoCore/TaskRunner/actions/workflows/dotnet.yml/badge.svg)](https://github.com/ShenHaoCore/TaskRunner/actions/workflows/dotnet.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

基于 Hangfire 的 .NET 9 任务调度。开发时 **Api 单进程**（入队 + 执行）；生产 Api 只入队/Dashboard，Worker 作为 Windows 服务执行。共享调度在 `TaskRunner.Hangfire`，Core 不引用 Hangfire。

## 运行（开发，PostgreSQL 容器）

```bash
docker run -d --name taskrunner-pg -e POSTGRES_USER=taskrunner -e POSTGRES_PASSWORD=TaskRunner!2026 -e POSTGRES_DB=taskrunner -p 15432:5432 postgres:17
dotnet user-secrets set "ConnectionStrings:Default" "Host=127.0.0.1;Port=15432;Database=taskrunner;Username=taskrunner;Password=TaskRunner!2026;" --project src/TaskRunner.Api
dotnet test
dotnet run --project src/TaskRunner.Api
```

`UserSecretsId` 与 Worker 共用，上面只需对 Api 设一次。开发环境 Api 角色为 `Combined`，**不必**再开 Worker。

- 首页：http://localhost:5088
- Dashboard：http://localhost:5088/taskrunner
- Scalar：http://localhost:5088/scalar
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

## PostgreSQL / 生产

只需一个数据库 `taskrunner` 与一个连接字符串 `ConnectionStrings:Default`：Hangfire 表位于同库的 `hangfire` schema，业务表 `TaskConfigs` 由 EF 迁移在启动时自动创建。公共配置在 `src/appsettings.json`（Api/Worker 构建时链接共享）；本地连接串用 User Secrets（见上方），生产用环境变量覆盖并设置 `Hangfire:AdminApiKey`。成功作业默认保留 60 分钟（`SucceededJobExpirationMinutes`）。

生产请单独运行 Worker（或安装 Windows 服务）：

```powershell
dotnet run --project src/TaskRunner.Worker
# 管理员
./scripts/install-worker-service.ps1
./scripts/uninstall-worker-service.ps1
```

Linux（Ubuntu）部署：打包 `./scripts/publish-linux.ps1`，服务器端 systemd 安装见 [docs/deploy-ubuntu.md](docs/deploy-ubuntu.md)。

## 项目结构

```
src/
  appsettings.json / appsettings.Development.json  # Api/Worker 共享配置
  TaskRunner.Api/        # 开发 Combined；生产 Client + Dashboard + REST
  TaskRunner.Worker/     # 生产 Server（Windows 服务 / systemd）
  TaskRunner.Hangfire/   # Hangfire 装配 + Console
  TaskRunner.Core/       # 任务契约与配置（无 Hangfire）
deploy/
  linux/                 # systemd 单元 + install.sh（随发布包带上服务器）
```

业务任务不进本仓库：实现 `IRecurringJob` 的程序集通过 `AddTaskRunnerCore(..., extraAssemblies)` 以插件方式注入。