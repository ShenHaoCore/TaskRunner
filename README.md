# TaskRunner

[![.NET CICD](https://github.com/ShenHaoCore/TaskRunner/actions/workflows/dotnet.yml/badge.svg)](https://github.com/ShenHaoCore/TaskRunner/actions/workflows/dotnet.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

基于 Hangfire 的 .NET 9 任务调度。开发时 **Api 单进程**（入队 + 执行）；生产 Api 只入队/Dashboard，Worker 作为 Windows 服务执行。共享调度在 `TaskRunner.Hangfire`，Core 不引用 Hangfire。

## 运行（开发，PostgreSQL 容器）

**推荐布局（Windows）**：PostgreSQL 装在本机 **WSL Ubuntu** 里，Windows 只跑 `dotnet run`；经 WSL localhost 镜像，连接串一般用 `Host=127.0.0.1`。须在 WSL 内放行 `pg_hba` / `listen_addresses`，并启用 systemd（装进 WSL 跑服务时）。逐步说明见 [docs/本地开发与虚拟机.md](docs/本地开发与虚拟机.md)。

```powershell
# Windows：PG 在 WSL（密码换成你的）
./scripts/setup-dev-secrets.ps1 -VmHost 127.0.0.1 -Password "你的密码"
dotnet test
dotnet run --project src/TaskRunner.Api
```

`setup-dev-secrets` 写入本机 User Secrets（Api/Worker 共用 `UserSecretsId`）。开发环境 Api 为 `Combined`，**不必**再开 Worker；与 WSL 内 `taskrunner-worker` **不要同时**连同一库抢 Server。

- 首页（仪表盘）：http://localhost:5088
- 任务运行台（过滤 + 一键运行）：http://localhost:5088/run
- Dashboard：http://localhost:5088/taskrunner
- 健康检查（业务库 + Hangfire 存储实探）：http://localhost:5088/health
- Scalar：http://localhost:5088/scalar
- 任务 API：http://localhost:5088/api/tasks

日志：`logs/taskrunner-*.log`（Api/Worker 共用文件，按日志中的 `Application` 列区分）。

## 鉴权与只读

| 环境 | 行为 |
|------|------|
| Development | Dashboard / 写操作免密钥 |
| Production | 配置 `Hangfire:AdminApiKey`（机器调用：头 `X-TaskRunner-Admin` 或 Basic）；浏览器用 `/login` 表单，凭 `Hangfire:AdminUsername` / `Hangfire:AdminPassword` 登录签发会话 Cookie（非明文密码） |

未登录的浏览器访问 `/taskrunner`、`/run` 等受保护页面时自动重定向到 `/login`（登录后回到原页面）；机器请求保持 `401`。只读由配置 `Hangfire:ReadOnlyMode` 控制（改配置需重启）。会话 Cookie 有效期 `Hangfire:SessionCookieDays`（默认 7 天，滑动过期）；登录接口按客户端 IP 限流 `Hangfire:LoginRateLimitPerMinute`（默认 5 次/分钟，仅生产生效）。应用端默认输出安全响应头（`X-Frame-Options: DENY`、`Content-Security-Policy: frame-ancestors 'none'`、`X-Content-Type-Options`、`Referrer-Policy`）；未捕获异常与 404 走统一错误页（页面渲染品牌化视图，`/api` 路径返回 ProblemDetails）。

## 运维页面（MVC）

自定义页面与 Hangfire Dashboard 同站、各司其职：首页（系统状态 + 最近失败，统计图表在 Dashboard）、`/login`、`/run` 运行台，均为 MVC 服务端渲染，采用 Linear 风格设计系统（深色默认，首访跟随系统偏好，右上角可切换）。未登录访问受保护页自动跳转登录页（带 returnUrl）；首页匿名仅展示骨架与登录引导。运行台支持按名称/队列过滤、一键触发、行内改 Cron（保存前预览下次执行）、勾选批量暂停/恢复/触发、任务行展开最近执行；页面按可见性感知自动刷新（编辑或展开详情时自动让路）。暂停任务禁用触发；时间显示按浏览器时区渲染。

## 新增任务

实现 `IRecurringJob` 并标记 `[RecurringTask]`；可选 `[ExclusiveExecution(秒)]`、`Queue`（默认 `default`）。扩展程序集启动时传给 `AddTaskRunnerCore(..., extraAssemblies)`。重启 Api 后自动注册。

```csharp
[RecurringTask("0 0 9 * * *", Description = "每日示例", Queue = "critical")]
[ExclusiveExecution(3600)]
public sealed class MyJob : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

Cron 支持 5/6 位。已存在任务的 Cron / 启用状态以 `TaskConfigs` 为准；队列来自契约标注（同步覆盖，不可运维改）。Worker 订阅列表配置 `Hangfire:Queues`（默认 `["default"]`），未订阅的队列会积压（启动时打错误日志）。作业应周期性响应取消；可配 `Hangfire:MaxJobRuntimeMinutes` 兜底单次运行时限（0=不限）。

运维 API：`GET /api/tasks` 支持 `page`/`pageSize` 分页（`X-Total-Count`）与 `If-None-Match` 304 缓存协商；错误响应统一 RFC 7807 ProblemDetails。`POST /api/tasks/{jobId}/trigger` 立即执行（异步受理，返回 **202** 与 queueJobId）；body 传 `{ "runAt": "..." }` 或 `{ "delaySeconds": 60 }` 预约一次；已暂停任务返回 409。启停/Cron 并发更新以 PostgreSQL `xmin` 行版本保护，冲突自动重试。浏览器可用 `/run` 过滤任务并一键运行（需先 `/login`）。

## PostgreSQL / 生产

调度轮询默认 5s（队列）/15s（周期任务变更），低延迟场景可在配置下调回 1s。只需一个数据库 `taskrunner` 与一个连接字符串 `ConnectionStrings:Default`：Hangfire 表位于同库的 `hangfire` schema，业务表 `TaskConfigs` 由 EF 迁移自动应用——**Api 与 Worker 都会执行「迁移 → 同步 recurring → 清理残留」**（EF 迁移以 PostgreSQL advisory lock 串行化），部署顺序不强依赖 Api 先行（也可选手跑 `./scripts/ef-database-update.ps1` / `.sh`）。公共配置在 `src/appsettings.json`；本地连接串用 User Secrets，生产用环境变量并设置 `Hangfire:AdminApiKey`。文件日志写在各宿主输出目录的 `logs/`（`BaseDirectory`）。成功作业默认保留 60 分钟（`SucceededJobExpirationMinutes`）；`TaskConfigs` 以 PostgreSQL `xmin` 行版本做并发保护。

### 多机部署（每机自带 PostgreSQL）

**发布包全网相同，禁止按机器改仓库或包内的 `appsettings*.json`。** 本机差异只放在该机的环境变量里：

| 场景 | 放哪里 | 不要改 |
|------|--------|--------|
| 开发 | User Secrets（见上方） | `src/appsettings*.json` |
| Linux 生产 | `/etc/default/taskrunner`（连接串）+ `taskrunner-api` / `taskrunner-worker` | `/opt/taskrunner/**/appsettings*.json` |
| Windows 生产 | 机器级环境变量（见 `scripts/install-worker-service.ps1` 提示） | 发布目录里的 `appsettings.json` |

建议各机约定：PG 在本机 `127.0.0.1:5432`、库/用户均为 `taskrunner`，**只有密码与 AdminApiKey 因机而异**。升级只覆盖程序目录，不动本机 env。本仓库当前不使用 Redis；若其他服务用本机 Redis，同样用该服务自己的本机 env，不要往本仓塞按机器分叉的 JSON。

生产请单独运行 Worker（或安装 Windows 服务）：

```powershell
dotnet run --project src/TaskRunner.Worker
# 管理员
./scripts/install-worker-service.ps1
./scripts/uninstall-worker-service.ps1
```

Linux / WSL 部署：

```powershell
# Windows → 本机 WSL（推荐）
./scripts/publish-linux.ps1
./scripts/deploy-wsl.ps1

# 或 SSH 到其它 Ubuntu
./scripts/deploy-ubuntu-vm.ps1 -Target user@host
```

操作说明书（Windows + WSL）：[docs/本地开发与虚拟机.md](docs/本地开发与虚拟机.md)。  
服务器端细节见 [docs/deploy-ubuntu.md](docs/deploy-ubuntu.md)。

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

工程约定：包版本集中在 `Directory.Packages.props`（中央包管理，csproj 不带版本号），还原以 `packages.lock.json` 锁定（CI `--locked-mode` + 漏洞审计门禁）；分层约束由架构守卫测试钉住（Core 零 Hangfire、Api 的 Hangfire 触点白名单），详见 `tests/TaskRunner.Tests/ArchitectureTests.cs`。