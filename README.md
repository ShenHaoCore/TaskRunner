# TaskRunner

基于 Hangfire 的 .NET 9 定时任务演示。Api 只负责创建任务、Dashboard 和 REST API；Worker 是独立的 Windows 服务，只负责执行任务。两者共用同一套 Hangfire 存储与业务库。共享调度代码在 `TaskRunner.Hangfire`，Core 不引用 Hangfire。

## 运行（开发环境，LocalDB）

```powershell
./scripts/ensure-localdb.ps1
dotnet test
dotnet run --project src/TaskRunner.Api
dotnet run --project src/TaskRunner.Worker
```

先启动 Api（迁移 + 注册 `[RecurringTask]`），再启动 Worker 执行任务。

- 首页：http://localhost:5088
- Dashboard：http://localhost:5088/taskrunner
- Scalar：http://localhost:5088/scalar
- 任务 API：http://localhost:5088/api/tasks
- 后台任务：http://localhost:5088/api/background-jobs
- 服务元数据：http://localhost:5088/info
- 登录（写 Cookie）：`POST /api/auth/login`，body `{"apiKey":"..."}`

开发环境默认 LocalDB（`(localdb)\mssqllocaldb`）。**不要**让 Api 与 Worker 双进程共享 SQLite Hangfire 库，否则容易 `database is locked` 甚至原生崩溃。若必须用 SQLite，把 Development 改回 `Storage=Sqlite`；Worker 会强制 `WorkerCount=1`。

日志：`logs/api/`、`logs/worker/`。

## 鉴权与只读

| 环境 | 行为 |
|------|------|
| Development | Dashboard / 写操作免密钥（认证处理器自动签发 Admin） |
| Production | 必须配置 `Hangfire:AdminApiKey`。写 API 需请求头 `X-TaskRunner-Admin: <key>`；Dashboard 可用浏览器 Basic（用户名任意，密码=ApiKey）或先调用 `/api/auth/login` 写 Cookie |

只读模式存在业务库 `RuntimeSettings`，Api/Worker 共享。首次启动由配置 `Hangfire:ReadOnlyMode` 种子写入；之后用 `PUT /api/tasks/settings/readonly` 修改即可两边同时生效。Dashboard Forms 与 REST 写操作都会检查该开关。

## 新增任务

在 `src/TaskRunner.Core/Jobs/Implementations/` 增加类，实现 `IRecurringJob` 并标记 `[RecurringTask]`。可选 `[ExclusiveExecution(60)]` 按 JobId 互斥。不要改 Api/Worker 的 `Program.cs`。重启 Api 后自动注册；代码里删除的任务会从 `TaskConfigs` 与 Hangfire recurring 清理。

```csharp
[RecurringTask("*/5 * * * * *", Description = "每5秒同步一次数据")]
[ExclusiveExecution(60)]
public sealed class SyncDataJob : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

Cron 支持 5 位或 6 位（含秒）。已存在任务的 Cron / 启用状态以 `TaskConfigs` 为准，重启不覆盖运维修改。`Parameters` 字段可通过 `PUT /api/tasks/{id}/parameters` 写入 JSON，供后续扩展。

## SQL Server / 生产

```powershell
# 或对目标实例执行 scripts/sqlserver-init.sql
./scripts/ensure-localdb.ps1   # 仅开发 LocalDB
```

`appsettings.json` 中 `Hangfire:Storage=SqlServer`，并设置 `Hangfire:AdminApiKey`。业务表由 EF 迁移创建（`TaskConfigs`、`SyncWindows`、`RuntimeSettings`）。

## Windows 服务

```powershell
# 管理员
./scripts/install-worker-service.ps1
./scripts/uninstall-worker-service.ps1
```

安装前请配置连接字符串（环境变量 `ConnectionStrings__Hangfire` / `ConnectionStrings__Default`，或编辑 publish 目录下的 `appsettings.json`）。也可用 NSSM，无需改代码。

## 项目结构

```
src/
  TaskRunner.Api/        # Client + Dashboard + REST
  TaskRunner.Worker/     # Server（Windows 服务）
  TaskRunner.Hangfire/   # Hangfire 装配（Api/Worker 共用）
  TaskRunner.Core/       # 任务接口与业务（无 Hangfire 引用）
```
