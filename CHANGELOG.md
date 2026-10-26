# Changelog

本项目的所有显著变更都记录在本文件。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循[语义化版本](https://semver.org/lang/zh-CN/)。

## 3.2.0 - 2026-10-27

### 升级到本版本要做什么

- **Linux（Ubuntu）成为一等部署目标**：systemd 单元 + 服务器端 install.sh + 一键打包脚本，完整流程见 [docs/deploy-ubuntu.md](docs/deploy-ubuntu.md)；Windows 服务行为不变
- **API 文档在生产环境开放为鉴权入口（行为变化）**：`/scalar` 与 `/openapi` 需管理员凭据（密钥头 / Cookie / Basic 任一，质询形态与 Dashboard 一致）——此前仅 Development 可访问
- **平台心跳任务默认启用**：Dashboard / TaskConfigs / `/api/tasks` 会多出一个 `heartbeat` 定时任务（每分钟）；不需要时配置 `Hangfire:HeartbeatEnabled=false` 并重启
- `/info` 新增 `environment`、`heartbeat` 字段（纯新增，现有调用方不受影响）
- Worker/Api 新增依赖 `Microsoft.Extensions.Hosting.Systemd` 9.0.22（自包含发布自动携带，无需部署动作）

### 新增

- Linux（Ubuntu）部署支持：Api/Worker 接入 systemd（`Type=notify` 就绪通知，非对应环境为 no-op，Windows 服务行为不变）；新增 `deploy/linux/`（systemd 单元 + 服务器端安装脚本 `install.sh`）、本地打包脚本 `scripts/publish-linux.ps1`（linux-x64 自包含单文件 tar.gz）与部署文档 `docs/deploy-ubuntu.md`
- 管理员登录页 `/login.html`：输入 `Hangfire:AdminApiKey` 写入 Cookie，免浏览器 Basic 弹窗直达 Dashboard；首页导航与鉴权提示联动入口
- API 文档在生产环境开放为「鉴权入口」：`/scalar` 与 `/openapi` 全环境映射，生产要求管理员凭据（密钥头 / Cookie / Basic 三形态任一，质询形态与 Dashboard 一致），`/info.scalar` 如实返回入口
- 平台心跳任务 `heartbeat`（每分钟，`Hangfire:HeartbeatEnabled` 默认开）：成功即证明「调度 → 入队 → 执行 → 状态写回」全链路与存储读写可用；`/info` 暴露心跳最近/下次执行时间，可在 Dashboard / REST 中像普通任务一样启停。Core 内置任务契约从「零内置」演进为「仅平台心跳」（`JobDiscoveryTests` 同步钉住）
- 配置环境区分：新增生产覆盖层 `appsettings.Production.shared.json`（构建时链接为 `appsettings.Production.json`，优先级介于公共默认与环境变量之间）；`/info` 暴露 `environment` 字段，首页导航徽章显示「环境 · 角色」

### 变更

- 首页/登录页重做并支持**双主题切换**：LeptonX Light（紫蓝品牌渐变，源自官方 `@volosoft/ngx-lepton-x` 主题包 token）与 Linear 暗色（近黑画布 + 薰衣草蓝）两套并存，导航栏一键切换、偏好本地记忆，两页面联动

## 3.1.0 - 2026-10-26

### 升级到本版本要做什么

- **安全收紧（行为变化）**：REST `/api/tasks` 全部端点在生产环境必须携带管理员密钥（header `X-TaskRunner-Admin` / cookie `TaskRunner.Admin` / Basic 三形态任一）——此前 GET 清单/统计/详情/历史与 PUT cron 匿名可访问。已携带密钥的调用方无感；匿名调用方升级后必须补齐
- 若以容器/服务方式部署且从未配置 `Hangfire:AdminApiKey`，升级后启动会直接失败（fail-fast）——补配密钥即可

### 变更

- **安全修复**：`TasksController` 管理员鉴权策略上提到类级，堵住 GET 全部端点与 PUT cron 匿名可访问的缺口
- 生产环境未配置管理员密钥时启动即失败：守卫从 Program 下沉为 `AdminKeyGuardStartupFilter`，随 `AddTaskRunnerAdminAuth` 自动生效
- 测试体系：39 个测试覆盖 Core 契约（23）、REST 运维主缝（9）与鉴权/只读安全面（7），此前 REST 层零测试覆盖
- 开发规格体系落地：`docs/specs/` 索引 + platform 规格（`spec.md`）与四张票（全部完成）

## 3.0.0 - 2026-10-26

### 升级到本版本要做什么

- **破坏性变更**：存储层从 SQL Server 切换到 PostgreSQL
  - 连接字符串改为 `Host=...;Port=...;Database=...;Username=...;Password=...` 格式，生产用 `ConnectionStrings__Default` 环境变量覆盖
  - 需准备 PostgreSQL 15+ 数据库（如 Docker：`docker run -d --name taskrunner-pg -e POSTGRES_USER=taskrunner -e POSTGRES_PASSWORD=... -e POSTGRES_DB=taskrunner -p 5432:5432 postgres:17`）
  - `TaskConfigs` 数据需迁移到新库（表小，SQL 导出导入即可）；Hangfire 作业历史不迁移，启动时自动重建 `public.TaskConfigs` 与 `hangfire` schema
  - `Hangfire:Storage` 键名不变，取值改为 `PostgreSql`（默认值即如此，一般无需配置）
- 其余能力不变：任务契约、发现、启用/Cron 运维、Dashboard、REST、鉴权、只读模式

### 变更

- 数据库从 SQL Server 切换到 PostgreSQL：EF 提供程序换为 Npgsql，Hangfire 存储换为 Hangfire.PostgreSql（schema `hangfire`），连接字符串改为 `Host=...;Port=...;Database=...;Username=...;Password=...` 格式

## 2.0.0 - 2026-10-26

### 升级到本版本要做什么

- **破坏性变更**：内置的 Bilibili 业务模块已删除。升级后 `Bilibili` 配置段失效、`/api/bilibili` 接口 404、`bilibili-daily` 不再注册；如需继续使用，从 [v1.1.0](https://github.com/ShenHaoCore/TaskRunner/releases/tag/v1.1.0) 源码拆出插件程序集，经 `AddTaskRunnerCore(..., extraAssemblies)` 注入
- 调度平台能力不受影响：任务契约、发现、启用/Cron 运维、Dashboard、REST、鉴权、只读模式均保留

### 变更

- 移除 Bilibili 业务模块及其接口/配置/页面：仓库回归纯调度平台，业务任务改由插件程序集经 `extraAssemblies` 注入

## 1.1.0 - 2026-10-26

### 升级到本版本要做什么

- 无破坏性变更：任务契约、配置键、数据库结构均未变动，按原方式重新部署 Api / Worker 即可
- 协作变化：`main` / `develop` 已启用分支保护（只走 PR、必需检查通过才可合入），CI 触发扩展到 `develop`

### 变更

- HTTP 层瞬时错误自动重试与封装优化
- 建立分支管理规范（Git Flow）：新增 CONTRIBUTING.md 与速查文档，CI 触发扩展到 develop

## 1.0.0 - 2026-09-28

- 初始化 TaskRunner：Api/Worker 双项目 Hangfire 调度
- B 站每日任务：扫码登录、视频三连、大会员权益、B 币券充电
- CI/CD：GitHub Actions 构建与测试；打 `v*` tag 自动发布 Api/Worker 的 win-x64 / linux-x64 单文件包到 Release
