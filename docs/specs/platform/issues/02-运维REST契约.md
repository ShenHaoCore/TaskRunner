# 02 用集成测试钉住 /api/tasks 运维契约

- 状态：`done`（2026-10-26；32 测试全绿（23 存量 + 9 新增），0 警告）
- Blocked by：01（Core 缝已钉牢，仓储语义可直接作为期望真源）
- 缝：REST `/api/tasks` 全部端点（开发规格确认的主缝）

## 交付什么

REST 主缝目前**零测试覆盖**——运维行为的端到端正确性只靠人工验证。
本票把清单、统计、历史、启停、Cron 覆盖、立即触发六组端点行为钉进集成测试
（真实 HTTP 管道 + SQLite 内存库，与仓储测试同法），修一个端点写一组用例。

## 现状

- 端点已实现并经发布验证（v1.1.0/v3.0.0 本地与 CD），但无护栏：改响应形状、
  改错误映射、改持久化语义，CI 不会红。
- 开发规格 Testing Decisions 明确主缝「能抓到」运维行为端到端正确性，
  本票就是主缝从提议变为事实的一步。

## 验收条件

- [x] 清单端点：返回全部任务，含 JobId、显示名、启用状态、生效 Cron、下次执行时间（另有 lastExecution/lastJobState/error 为 null 而非缺失的形状断言）
- [x] 生效 Cron：运维覆盖值优先于特性默认值（与仓储 Sync 语义一致，重放发现结果模拟重启后覆盖值仍在）
- [x] 启停端点：写库生效（pause/resume 后 isEnabled 翻转），重启进程后状态保持（重放 Sync 不复活）
- [x] Cron 覆盖端点：合法表达式落库生效；非法/越界表达式（4/7 字段、空串、乱写）被拒 400 并返回明确 message，库值不变
- [x] 立即触发端点：入队成功有可观察证据（响应含 queueJobId + fake 调度器记录到 Enqueue 调用）
- [x] 统计与历史端点：统计七计数字段齐全；空历史返回空数组不炸
- [x] 不存在的任务名 → 404 语义；错误响应形状统一（get/history/trigger/pause/cron 五入口全部 404 + message）

## 实现

- 测试宿主 `ContractApp`：复刻 Program 装配（`AddTaskRunnerCore` + `AddTaskRunnerAdminAuth` + `UseTaskRunnerPipeline`，过滤器/认证/路由 100% 真实），存储换测试件：SQLite 文件库 + Hangfire MemoryStorage + 记录式 `ITaskScheduler`；不注册 `RecurringJobBootstrapper`，迁移与种子由宿主/测试自管。
- 踩坑记录：Mvc.Testing 的 `WebApplicationFactory` 对本仓库 ContentRoot 推断指向不存在目录（`<sln 根>/TaskRunner.Api`）且传播不稳，弃用改 `TestServer` 手动装配；`AddTaskRunnerHangfire` 配置委托会立即解析 PostgreSql 连接串，测试宿主不能调用；controller 需 `AddApplicationPart` 显式挂 Api 程序集（entry 是 testhost）；environment 必须经 `WebApplicationOptions` 传入（Build 前不可改 host 配置）。

## 不在本票范围

- 鉴权与只读拦截（票 03，避免一张票同时改认证与端点两组断言）；
- Dashboard 渲染细节（主缝明确漏掉的盲区）。
