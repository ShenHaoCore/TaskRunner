# Changelog

本项目的所有显著变更都记录在本文件。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循[语义化版本](https://semver.org/lang/zh-CN/)。

## 3.7.1 - 2026-10-29

### 修复

- 未登录的浏览器访问 Hangfire Dashboard（`/taskrunner`）时 302 重定向到 `/login`（携带 returnUrl，登录后回到原页面）；机器请求（Accept 非 text/html）保持 401 + Basic 质询。此前浏览器会弹出 Basic 认证框

## 3.7.0 - 2026-10-29

### 升级到本版本要做什么

- **登录改为账号密码**：存量机器需在 `/etc/default/taskrunner-api` 手动补 `Hangfire__AdminUsername`（缺省 `admin`）与 `Hangfire__AdminPassword` 后重启 taskrunner-api；未补则 `/login` 表单给出引导（机器密钥调用不受影响）。新装由 install.sh 模板生成占位
- 旧 `*.html` 书签自动重定向到 `/run`、`/login`，无需更新
- 自定义页面样式整体更换为 Linear 设计系统（深色为默认），若引用过旧页内 CSS 类需留意

### 变更

- 登录鉴权改账号密码：`Hangfire:AdminUsername` / `Hangfire:AdminPassword`；校验两侧 SHA-256 后恒定时间比较（不泄漏长度）；`/api/auth/login` 契约改为 `{username, password}`；机器调用保持 `AdminApiKey`（`X-TaskRunner-Admin` / Basic）不变
- MVC 登录：`GET/POST /login`（表单 + 防伪令牌 + 按 IP 限流）、`POST /logout`；导航栏显示当前账号
- 页面重做为 Linear 设计系统（token 源自 awesome-design-md · linear.app：#010102 画布 + 四级 surface 阶梯 + 发丝线 + 薰衣草蓝唯一强调；浅色为反相 token；Inter 替代字体，深色为默认）
- 页面数据服务端化：首页统计/心跳、运行台列表/过滤/触发（MVC 表单 + 一次性提示条）全部 Razor 渲染，删除 fetch 型 JS 与分页 css（合并为 `app.css`）
- 只读模式下运行台触发按钮禁用并提示
- install.sh 新装模板含账号密码占位；部署文档环境变量表同步

## 3.6.0 - 2026-10-28

### 升级到本版本要做什么

- Linux：重跑 `install.sh` 后配置拆为 `/etc/default/taskrunner`（连接串）+ `taskrunner-api` / `taskrunner-worker`；旧单文件会自动迁移拆分。填完 `CHANGE_ME` 再 start
- 运维页路径改为 `/` `/login` `/run`（MVC）；旧 `*.html` 书签失效

### 变更

- 运维页改为 MVC（`/` `/login` `/run`）共享 `_Layout`，保留 Hangfire Dashboard 同款风格；去掉静态 `*.html`
- 生产 env 按角色拆分：共享连接串 + Api 鉴权 + Worker 队列/并发；AdminApiKey 不再进入 Worker 单元

## 3.5.0 - 2026-10-28

### 升级到本版本要做什么

- 重跑 `install.sh` 即可完成升级：systemd unit 会重写并加入安全硬化指令（`NoNewPrivileges`、`ProtectSystem=full` 等）
- 对外经 nginx 暴露时，建议按部署文档补上安全响应头（CSP 等，含现成配置示例）
- 可选：配置 `Hangfire__HeartbeatPingUrl` 接入 healthchecks.io 类死信告警
- 自定义页面样式整体更换为 Hangfire Dashboard 风格，若有引用页内 CSS 类的外部脚本需留意

### 变更

- 心跳死信开关：`Hangfire:HeartbeatPingUrl` 配置后心跳成功即 POST，ping 失败计入重试/Failed，外部服务「未按时收到 ping」即告警
- 自定义页面（首页/运行台/登录）改为 Hangfire Dashboard 同款风格：浅色为官方经典款、深色为 1.8 官方暗色（token 取自 Hangfire.Core 1.8.25 内嵌 CSS）；移除 Google Fonts 外链
- 作业控制台日志升级：▶/✔/✘ 符号、灰/绿/红着色与真实耗时，失败原因直接写入作业详情页
- 版本注入程序集：发布流水线随 tag 写入 `InformationalVersion`，`/info` 新增 `version` 字段，首页页脚展示
- systemd 硬化：Api/Worker unit 增加 `NoNewPrivileges`、`ProtectSystem=full`、`PrivateTmp` 等最小权限指令
- 部署脚本上传包 SHA256 校验：WSL（9P 陈旧读取）与 scp 两条链路均覆盖，不一致自动重试一次后中止
- 部署文档：nginx 安全响应头（CSP）示例、PostgreSQL 每日定时备份（pg_dump）与恢复说明

## 3.4.0 - 2026-10-27

### 升级到本版本要做什么

- Api 启动会迁移 `TaskConfigs.Queue` 列（也可选手跑 `ef-database-update`）
- 若使用非 `default` 队列：在任务上标注 `Queue`，并配置 Worker 的 `Hangfire:Queues` 包含该名，否则作业积压

### 变更

- 多队列：`[RecurringTask(Queue=...)]` + `Hangfire:Queues`；清单字段 `queue`；周期/触发入声明队列（票 05）
- 定时一次触发：`POST .../trigger` 支持 `runAt` / `delaySeconds`（票 06）
- 任务运行台：`/run.html` 过滤 + 一键立即运行（票 07）

## 3.3.3 - 2026-10-27

### 升级到本版本要做什么

- **登录 Cookie 形态变化**：`/api/auth/login` 改为 ASP.NET 会话票据，Cookie 中**不再**是 ApiKey 明文；仍用旧「Cookie=密钥」方式的客户端需改为先 login，或改用 Header / Basic
- 文件日志默认写在各宿主输出目录 `logs/`（`BaseDirectory`）；需要仓库根日志时用配置绝对路径覆盖
- 可选：发布前选手跑 `./scripts/ef-database-update.ps1` / `.sh`（Api 启动仍会 `MigrateAsync`）

### 变更

- 鉴权：`/api/auth/login` 改为 ASP.NET Cookie 会话票据（不再把 ApiKey 明文写入 Cookie）；Header / Basic 不变；`MatchesKey` 长度不一致时安全返回 false
- 日志：Serilog 文件路径解析到 `AppContext.BaseDirectory/logs`，去掉依赖查找 `.sln` 的 `SetCurrentDirectory`
- 迁移：保留 Api 启动 `MigrateAsync`；新增 `scripts/ef-database-update`（ps1/sh）供显式迁库；platform 票 03 / spec 同步 Cookie 会话语义

## 3.3.2 - 2026-10-27

### 升级到本版本要做什么

- Windows + WSL 布局以 [docs/本地开发与虚拟机.md](docs/本地开发与虚拟机.md) 为准：PG 在 WSL 时 Secrets 用 `Host=127.0.0.1`；装进 WSL 用 `publish-linux.ps1` + `deploy-wsl.ps1`（需启用 systemd）

### 变更

- 说明书与脚本按 **WSL Ubuntu** 为主路径调整：Windows 经 localhost 连 WSL 内 PG；新增 `deploy-wsl.ps1`；`docs/本地开发与虚拟机.md` 改为 WSL + systemd 步骤

## 3.3.1 - 2026-10-27

### 升级到本版本要做什么

- **Linux 安装**：重新执行 `install.sh` 后，若 `/etc/default/taskrunner` 仍含 `CHANGE_ME`，systemd 会在 `ExecStartPre` 拒绝启动——只改该 env 文件，不要改 `/opt/taskrunner/**/appsettings.json`
- 多开发机 / 本机 Ubuntu 虚拟机（PG 在 VM 内）逐步操作见 [docs/本地开发与虚拟机.md](docs/本地开发与虚拟机.md)；宿主机可用 `setup-dev-secrets` / `publish-linux` / `deploy-ubuntu-vm` 脚本

### 变更

- 多机配置约定落地：README / deploy-ubuntu 明确「发布包相同，只改本机 env / User Secrets，禁止改包内 appsettings」；Linux 单元增加 `ExecStartPre`，`/etc/default/taskrunner` 仍含 `CHANGE_ME` 时拒绝启动；Windows Worker 安装脚本改为只引导机器级环境变量
- 开发/发布脚本补齐：`setup-dev-secrets`（ps1/sh）、Mac/Linux 用 `publish-linux.sh`、宿主机一键装进 Ubuntu VM 的 `deploy-ubuntu-vm`（ps1/sh）
- 新增操作说明书 [docs/本地开发与虚拟机.md](docs/本地开发与虚拟机.md)（多开发机 + 本机 Ubuntu 虚拟机）

## 3.3.0 - 2026-10-27

### 升级到本版本要做什么

- **配置文件布局变更**：公共默认改为 `src/appsettings.json`（构建时链接到 Api/Worker），已移除 `*.shared` 命名与 `appsettings.Production.json` 覆盖层；生产差异（含原生产 JSON 里的非敏感项）一律走环境变量（如 `/etc/default/taskrunner`）
- **本地开发连接串**：不再写入仓库，需执行 `dotnet user-secrets set "ConnectionStrings:Default" "..."`（Api/Worker 共用 `UserSecretsId`，对 Api 设一次即可）；步骤见 README
- **TLS 反代部署**：若管理员登录 Cookie 需带 Secure，配置 `Hangfire:CookieSecure=true` 并重启
- 登录接口生产环境按 IP 限流（5 次/分钟，超出 429）；Development 不限
- 重新安装/更新 Linux 单元后 Worker 会排在 Api 之后启动（规避双进程首启迁移竞态）

### 新增

- 安全加固：管理员登录接口按 IP 限流（5 次/分钟，超出 429；Development 不限），密钥比较改为恒定时间（防时序侧信道）；管理员 Cookie 新增 `Hangfire:CookieSecure`（TLS 反代部署置 true）
- 部署：Worker systemd 单元排序在 Api 之后，规避双进程首启同时跑迁移的竞态
- 测试：文档守卫（/scalar、/openapi）、/info 字段（environment/heartbeat）、登录限流、心跳目录开关的契约测试钉板

### 变更

- 配置收敛：共享配置改为标准命名 `src/appsettings.json` + `src/appsettings.Development.json`（Api/Worker 链接共享）；去掉 `*.shared` 命名、各项目空壳 `appsettings.json` 与生产 JSON 覆盖层，生产差异统一走环境变量
- 本地开发连接串改为 User Secrets（Api/Worker 共用 `UserSecretsId`），不再写入仓库；Worker 启动日志改为读取 `TaskRunnerOptions.Storage`，去掉错误的 `SqlServer` 回退
- CD：打 tag 发布时自动把 CHANGELOG 对应版本的「升级到本版本要做什么」写入 Release 说明顶部（原手工步骤移除）
- 依赖机器人（Dependabot）目标分支由 main 改为 develop，并忽略 `Microsoft.*` / `Npgsql*` 的主版本跳变（9→10 需整体 TFM 迁移决策）——此前两者分叉导致 release 合并冲突

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
