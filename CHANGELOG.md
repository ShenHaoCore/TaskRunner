# Changelog

本项目的所有显著变更都记录在本文件。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循[语义化版本](https://semver.org/lang/zh-CN/)。

## 3.11.0 - 2026-10-31

发布后全量代码审查（双代理 + 真机浏览器验证）的修复批次；测试 127 → 136。

### 升级到本版本要做什么

- 从 3.10.0 升级无数据库迁移，直接替换程序文件即可
- **3.10.0 的两处功能缺陷在本版修复**：运行台批量「暂停」此前必然失败（请求丢 `action` 字段）；详情面板/Cron 预览的动态时间不渲染——依赖这两处的操作请在升级后重新执行
- 只读模式（`Hangfire:ReadOnlyMode=true`）行为变化：登录/登出不再被 403 拦截（认证流放行，任务写操作照拦）
- **孤儿任务删除新增 10 分钟宽限期**：滚动升级期间 Api/Worker 版本短暂不一致时，旧进程不再立即删除自己发现集里没有的任务（防误删新版本刚注册的任务）；真废弃任务在宽限后照常清理
- `/api/tasks` 的 ETag 指纹改为 tick 级并纳入分页参数：依赖 If-None-Match 缓存的客户端升级后首次请求会得到 200（新指纹）
- 机器客户端的 401 现在携带 `WWW-Authenticate: Basic realm="TaskRunner"` 头
- 本地开发注意：修复了共享配置装配误删 user secrets 源的缺陷——此前本地 User Secrets 实际从未生效，升级后本地连接串以 User Secrets 为准（请确认其中值正确）

### 修复

- **批量操作「暂停」必然失败**：防重处理器禁用了第一个提交按钮（正是暂停提交器），而禁用字段不进 FormData——请求丢失 `action` 字段被服务端判为「未知操作」；恢复/触发则存在双击重复提交。现先把提交器 name/value 写入隐藏域再禁用全部按钮
- **运行台（可操作模式）自动刷新从不触发**：`interactionOpen` 查 `.cron-editor:not([hidden])`，而 `hidden` 在父行 `<tr class="cron-row">` 上——恒判「交互中」短路两条刷新路径；并新增「焦点在输入控件内不刷新」守卫
- **Cron 预览过期响应竞态**：连续编辑时慢的旧请求后到会覆盖新表达式的预览——引入请求序号，过期响应丢弃
- **`/api/tasks` ETag 误命中 304**：旧指纹「数量+最大 UpdatedAt 秒级截断」——同一秒内连续两次写入客户端拿到过期缓存，且不同分页共用 ETag；改为排序后 `(JobId, UpdatedAt.Ticks)` 哈希 + 分页参数，304 分支补 `X-Total-Count`
- **只读模式拦死认证流**：`ReadOnlyGuardFilter` 把 POST /login、/api/auth/* 一并 403，只读模式下无法登录也无法登出——认证流路径放行，任务写操作照拦
- **`/error` 把 401/403/429 标成「服务器内部错误」**：按状态段给语义标题（需要认证/没有权限/请求过于频繁/…）
- **机器客户端 401 无 `WWW-Authenticate`**：与 Dashboard/openapi 守卫不一致，Basic 方案不可发现——补 `Basic realm="TaskRunner"`
- **advisory lock 等待受 30s CommandTimeout 限制**：并发首启遇到长迁移会超时崩溃而非等待——锁获取/持有期间 CommandTimeout=0，退出恢复
- **滚动升级版本偏斜静默删任务**：Api 新版 + Worker 旧版并行重启时，旧进程发现集不含新任务 → 删配置行 + 摘 recurring，新任务凭空消失——`RemoveMissingAsync` 增加 10 分钟宽限期，prune 保留清单并入库中现存行
- **日志清理单文件失败中断整轮**：被占用/只读文件抛异常终止后续扫描且统计失真——逐文件容错，汇总单列「失败 N 个」
- **平台自检探针残留**：写删之间异常留下 `.self-check-*.tmp` 永不回收——finally 兜底清理
- **flash 监听悬挂**：dismiss 后 document 级 visibilitychange 监听不解绑——AbortController 随元素移除解绑

全面质量轮：后端正确性修复、前端安全与体验、自定义页面精简与增强、工程化与 CI 门禁、平台自带任务集；测试 78 → 127 全绿。

### 升级到本版本要做什么

- **四个平台自带任务随本版本启用**：日志清理（log-cleanup，每天 03:30 删除 `Hangfire:LogRetentionDays`（默认 14）天前的滚动日志）、每日运行日报（daily-digest，08:00）、平台自检（self-check，06:00）默认启用；演示模板（demo-progress）默认禁用——不想要某任务可在运行台暂停
- **仓库新增 `NuGet.config`（单一 nuget.org 源）**：本机全局配置的镜像源对本仓库不再生效；网络受限时把该文件里的源换成你的镜像
- **锁定还原已启用**：`packages.lock.json` 随仓库提交，CI 锁定模式还原——改动依赖需同步提交 lock 文件（本地 `dotnet restore` 自动更新）
- 应用端默认输出 `X-Frame-Options: DENY` 与 `Content-Security-Policy: frame-ancestors 'none'`：如有系统需要 iframe 嵌入本平台页面，需调整该中间件（Hosting/WebApplicationExtensions）
- 首页监控图表（指标卡/趋势图）移除，统计详情以 Hangfire Dashboard 为准；首页保留健康信号与最近失败；运行台新增行内改 Cron/批量操作/自动刷新
- 可选新增配置：`Hangfire:SessionCookieDays`（会话 Cookie 有效期天数，默认 7）、`Hangfire:LoginRateLimitPerMinute`（登录限流，默认 5 次/分钟）——不配置行为与原默认一致
- `deploy-ubuntu-vm.ps1` 现在会对上传包做 SHA256 对账校验（与 .sh 版对齐；此前 CHANGELOG 3.5.0 的声明在 Windows 链路上未兑现）

### 修复

- 心跳死信 ping 的 10s HttpClient 超时从未生效：作业此前注入的是默认客户端（100s 超时），外部服务挂起时会长时间占住 Worker——改为经 `IHttpClientFactory` 取命名客户端
- Api/Worker 双进程并发首启竞态：advisory lock 此前只包住迁移，「同步配置 + 清理残留」仍在锁外，两侧同时插入相同 JobId 会撞唯一索引并使宿主启动失败——整个启动编排移入会话级锁
- 队列覆盖校验误报：配置侧未归一化（如值带尾部空白）会假报「队列未订阅」——两侧统一经 `QueueNameGuard.NormalizeSet`
- 互斥执行过滤器类型解析失败时静默放弃互斥（并发执行可能真实发生）——现记录 Warning
- 同一次作业失败不再重复记两层 Error（JobExecutor 层移除，保留 Gateway 层带耗时与 Console 输出的记录）
- `GET /api/tasks` 的 304 快路径前置：命中缓存协商时不再白读一遍 Hangfire 监控数据
- 运行台防重复提交不再「3 秒后无条件恢复」（慢网络下可重复入队），禁用保持到页面跳转
- 成功提示条：页面不可见时暂停计时（后台标签页不再白走 5 秒），悬停移出后恢复计时
- `mark.svg` 引用补 `asp-append-version`（静态资源为一年 immutable 缓存，此前更新 logo 会全站拿到旧图）
- 浅色主题次要文本对比度 3.2:1 → ≥4.5:1（WCAG AA）
- `install-worker-service.ps1`：服务停止改为轮询确认后再删除，消除固定 2 秒等待的停删竞态

### 新增

- 平台自带任务集（新插件程序集 `src/TaskRunner.PlatformJobs`，Api/Worker 双侧注入，运行台列表直接可见/可触发）：
  - **日志清理**（log-cleanup，每天 03:30）：删除超过保留期的滚动日志文件（新配置 `Hangfire:LogRetentionDays`，默认 14 天），带互斥锁
  - **每日运行日报**（daily-digest，每天 08:00）：累计统计快照 + 最近失败 top5 写入作业详情页与日志；新抽象 `IJobInsights`（Core）+ `HangfireJobInsights`（引擎侧实现，ITaskScheduler 同款模式）
  - **平台自检**（self-check，每天 06:00）：业务库连通、日志目录可写、心跳组合配置、暂停任务概况；只告警不失败
  - **演示模板**（demo-progress，每 5 分钟，默认禁用）：20 步进度 + 分阶段输出 + 15% 失败演示重试链路，供接入方抄模板
- 自定义页面精简与增强（参考 Hangfire.Community.Dashboard.Forms / RecurringJobAdmin 的任务管理 UI 设计）：
  - 首页轻量化：与 Dashboard 同源的统计卡/趋势图/入口卡移除，保留差异化健康信号（Worker 在线、心跳、排队、失败计数）与「最近失败」列表（仅失败，与运行台的全量最近执行差异化）；hero 只留 Dashboard/运行台主入口
  - 运行台行内改 Cron：✎ 展开行内表单，保存前预览未来 5 次执行（新只读端点 `GET /api/tasks/cron-preview`，解析与启停/改 Cron 同一套 Cronos 规则）
  - 任务行「详情」展开最近执行（复用现有 history REST，展示 Succeeded/Failed 结局与原因）
  - 按队列过滤 + 勾选批量暂停/恢复/触发（恢复前校验 Cron，flash 汇总成败）
  - 可见性感知自动刷新（30s）：后台标签页暂停、回前台补刷，Cron 编辑或详情展开时自动让路
- 统一错误页：未捕获异常与无端点命中的 404 走品牌化错误页（`/error`、`/error/{code}`）；`/api` 路径与机器客户端返回 ProblemDetails JSON
- 安全响应头兜底：`X-Frame-Options: DENY` + `Content-Security-Policy: frame-ancestors 'none'`（应用端默认输出，反代可再收紧完整 CSP）
- 前端交互脚本外置为 `wwwroot/js/app.js`（带版本号长缓存），为主题脚本内联保留防 FOUC
- 禁用触发按钮的禁用原因以可见文字呈现（此前仅 title，屏幕阅读器拿不到）
- 测试盲区补齐：ExclusiveJobFilter 分支、启动编排各分支、PruneRecurringExcept 真实现（MemoryStorage）、重试退避序列、队列归一化、`/health` 契约、JobExecutor 缺失/非契约类型分支、UpdateAsync

### 变更

- 工程化：中央包管理（`Directory.Packages.props` + 仓库级 `NuGet.config` 单源）与锁定还原（`packages.lock.json`，CI 锁定模式 + NuGet 缓存）；TargetFramework 等公共构建属性收进 `Directory.Build.props`；`Microsoft.NET.Test.Sdk` 升级 17.14.1（仅测试工具链）
- 质量基建：`EnforceCodeStyleInBuild`（.editorconfig 风格规则构建时生效，存量经 dotnet format 清理）+ `AnalysisLevel latest-recommended`（豁免集中在 .editorconfig 注明理由）；架构守卫补强——Core 零 Hangfire、Api 的 Hangfire 触点白名单、中央包管理版本纪律
- CI：并发取消组、format 校验与依赖漏洞审计（Critical/High 即失败）门禁、Release 创建拆分为前置 job（消除 4 矩阵位竞态）
- 导航收敛：nav 移除「任务 API」（机器调用走 `X-TaskRunner-Admin` 头，交互文档在 `/scalar`），「仪表盘」更名「总览」消除与 Dashboard 的歧义
- `HangfireJobGateway` 使用原生 `CancellationToken` 注入（替换过时的 `IJobCancellationToken`）
- `HangfireTaskScheduler` 改为构造注入 `JobStorage`（替换静态 `JobStorage.Current`）
- 启动序列减少两次全量读取（同步结果直接复用；Resume 接口消掉同一行双查）
- 散落魔法数字归位：`SessionCookieDays`/`LoginRateLimitPerMinute` 入配置，其余（过期扫描、心跳间隔、退避上限、Dashboard 轮询）提为具名常量
- 「浏览器请求跳登录」与 Cron 校验的重复实现收敛为公共辅助

## 3.9.1 - 2026-10-30

### 修复

- 3.9.0 的 xmin 并发令牌无需迁移（系统列），但 EF9 会将其误报为「模型有未迁移变更」导致 Api/Worker 启动即失败——忽略该误报告警，恢复正常启动

## 3.9.0 - 2026-10-30

上批审查记录的架构项全部落地。

### 升级到本版本要做什么

- 调度轮询默认值调大：队列 1s→5s、周期任务变更 1s→15s（显式配置过的不受影响）；低延迟场景可在配置下调回
- 运行台/首页的时间改为按浏览器时区渲染（服务端输出 UTC ISO）——此前按服务器时区显示
- Worker 现在也会执行「迁移 → 同步 recurring」（与 Api 并行启动安全：advisory lock 串行化），部署顺序不再强依赖 Api 先行

### 新增

- `TaskConfigs` 并发令牌（PostgreSQL xmin）：启停/Cron 并发更新冲突自动重载重试，不再静默丢失更新
- EF 迁移以 PostgreSQL 会话级 advisory lock 串行化
- `GET /api/tasks` 分页（`page`/`pageSize`，0=全量）+ `X-Total-Count` + `If-None-Match` 304 缓存协商

### 变更

- 调度轮询默认值：队列 5s、周期任务变更 15s
- 页面时间显示改为浏览器时区（`time[data-local-time]` + 前端格式化）

## 3.8.0 - 2026-10-29

全面代码审查（对照 Hangfire 官方建议与高星调度平台惯例）后的改进批次。

### 升级到本版本要做什么

- API 错误响应改为 RFC 7807 ProblemDetails（`detail` 字段取代 `message`）；触发作业返回 **202**（原 200），已暂停任务返回 **409**——依赖旧行为的脚本需同步调整
- `/info` 匿名访问只保留最小暴露（name/status/version/tasks/authLogin），完整字段需管理员凭据
- 可选新增 `Hangfire:MaxJobRuntimeMinutes`（单作业运行时限，0=不限，默认不限）

### 新增

- `/health` 真实健康检查：业务库连通 + Hangfire 存储可读（原为静态 ok）
- 作业运行时限：超时以取消令牌通知作业，防单个失控任务占死 Worker
- 反代支持：信任本机 nginx 的 Forwarded 头，登录限流按真实客户端 IP 分桶
- 页面级安全响应头（X-Content-Type-Options / Referrer-Policy）
- 运行台触发防重复提交；暂停任务在页面上禁用触发按钮

### 修复

- 插件程序集部分类型加载失败时中止任务发现，不再基于残缺结果清理任务配置（防误删 Cron 覆盖与启停状态）
- 重试退避按重试次数生成（2/4/8/16…上限 1h），不再固定 [2,4,8]
- 契约声明队列未被 Worker 订阅时启动打错误日志（原为静默滞留 Enqueued）
- 匿名 `/info` 暴露裁剪；主题首访跟随系统偏好；移动端导航可见；静态资源长缓存；可访问性补齐（aria-label/aria-current/aria-pressed）

## 3.7.4 - 2026-10-29

### 变更

- 运行台成功提示（如「已入队 …」）5 秒后自动淡出（悬停暂停计时，尊重减少动态偏好）；失败提示保持常驻

## 3.7.3 - 2026-10-29

### 修复

- 匿名浏览器访问受保护页面（如 `/run`）不再返回空白 401，而是 302 重定向到 `/login`（携带 returnUrl，登录后回到原页面）；API/机器请求保持 401

## 3.7.2 - 2026-10-29

### 修复

- 运行台触发后的提示条与下方「运维」眉题贴死：补 flash 下边距（20px）

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
