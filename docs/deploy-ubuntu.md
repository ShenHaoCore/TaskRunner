# Ubuntu 部署指南

生产形态：**Api**（Hangfire Client + Dashboard + REST，只入队）+ **Worker**（Hangfire Server，执行作业）。
两者都用 systemd 管理，以专用系统用户 `taskrunner` 运行，安装到 `/opt/taskrunner`，配置集中在 `/etc/default/taskrunner`。

## 前置要求

- Ubuntu 22.04 / 24.04（其他发行版需自行调整 `apt` 命令）
- PostgreSQL 15+ 可达（可以不在本机）：一个空库 `taskrunner` 即可，业务表与 Hangfire schema 启动时自动创建
- 发布包（见下节）。自包含形态无需在服务器装 .NET；框架依赖形态需要 `apt install -y dotnet-sdk-9.0`

## 获取发布包

**方式 A：本地一键打包**（任意装有 .NET 9 SDK 的宿主机；产物自包含单文件，零运行时依赖）

```powershell
# Windows
./scripts/publish-linux.ps1
# 产物：artifacts/taskrunner-<版本>-linux-x64.tar.gz（含 api/、worker/、deploy/）
```

```bash
# Mac / Linux
./scripts/publish-linux.sh
```

**方式 B：GitHub Release 下载**

从 Releases 下载 `TaskRunner.Api-linux-x64.zip` 与 `TaskRunner.Worker-linux-x64.zip`，解压拼成目录：

```
taskrunner/          # 包根目录
├── api/             # ← TaskRunner.Api-linux-x64.zip 解压到这里
├── worker/          # ← TaskRunner.Worker-linux-x64.zip 解压到这里
└── deploy/          # ← 仓库 deploy/linux/ 整个目录拷贝到这里
```

> 注意：3.1.0 及更早的 Release 产物没有 systemd 就绪通知（`Type=notify`）支持，
> 配这种包需用 `sudo TASKRUNNER_SERVICE_TYPE=simple bash ./deploy/install.sh` 安装，否则服务启动会超时失败。

## 安装

**一键（推荐，本机 Ubuntu 虚拟机或远程机均可）**

```powershell
# Windows 宿主机（先 publish-linux.ps1）
./scripts/deploy-ubuntu-vm.ps1 -Target user@192.168.56.10
```

```bash
# Mac / Linux 宿主机（先 publish-linux.sh）
./scripts/deploy-ubuntu-vm.sh user@192.168.56.10
```

**手动**

```bash
# 上传并解压
scp artifacts/taskrunner-<版本>-linux-x64.tar.gz user@server:/tmp/
ssh user@server
mkdir -p /tmp/taskrunner && tar -xzf /tmp/taskrunner-*-linux-x64.tar.gz -C /tmp/taskrunner

# 安装（幂等，可重复执行用于升级）
sudo bash /tmp/taskrunner/deploy/install.sh
```

脚本做的事：创建系统用户 `taskrunner` → 停旧服务 → 覆盖 `/opt/taskrunner/{api,worker}` →
安装 `/etc/systemd/system/taskrunner-{api,worker}.service` → 生成 `/etc/default/taskrunner` →
设置开机自启（**不立即启动**，等配置填好）。

## 生产配置（必填两处）

编辑 `/etc/default/taskrunner`（systemd EnvironmentFile，权限 600）：

```bash
sudo nano /etc/default/taskrunner
```

| 变量 | 必填 | 说明 |
|------|:----:|------|
| `ConnectionStrings__Default` | ✅ | PostgreSQL 连接串，如 `Host=127.0.0.1;Port=5432;Database=taskrunner;Username=taskrunner;Password=...` |
| `Hangfire__AdminApiKey` | ✅ | 管理员密钥。**未配置时 Api 生产环境启动即失败**（fail-fast） |
| `ASPNETCORE_URLS` | 可选 | 默认 `http://127.0.0.1:5088`（仅本机，建议配 nginx 反代；要公网直连改 `http://0.0.0.0:5088`） |
| `Hangfire__ReadOnlyMode` | 可选 | `true` 时所有写操作（含 Dashboard）被拦截，适合灾备只读实例 |
| `Hangfire__WorkerCount` | 可选 | Worker 并发数，默认 5 |
| `Hangfire__CookieSecure` | 可选 | TLS 反代部署置 `true`（管理员 Cookie 仅经 HTTPS 传输），默认 `false` 兼容本机 http |

启动并验证：

```bash
sudo systemctl start taskrunner-api taskrunner-worker
systemctl status taskrunner-api taskrunner-worker
curl -H "X-TaskRunner-Admin: <你的密钥>" http://127.0.0.1:5088/api/tasks
```

## 配置的环境区分

配置优先级（低 → 高）：`appsettings.json`（公共默认，源 `src/appsettings.json`）→ 环境变量 `/etc/default/taskrunner`（密钥与连接串、以及生产差异如 Worker 数等）。
本地开发用 User Secrets（见 README），不要改仓库里的连接串。改完 EnvironmentFile 需 `systemctl restart` 才生效。
实例当前环境可通过 `/info` 的 `environment` 字段确认（首页导航徽章同步显示）。

### 多机：包相同，只改本机 env

每台机器可以自带 PostgreSQL（甚至本机 Redis 给其他服务用），但 **TaskRunner 发布包与仓库配置保持一份**：

1. 所有机器安装同一份 tar.gz / 同一套 `api/`、`worker/`。
2. **只**编辑该机的 `/etc/default/taskrunner`（填本机 PG 连接串与 `Hangfire__AdminApiKey`）。
3. **不要**改 `/opt/taskrunner/api/appsettings.json` 或 `worker` 下同名文件——升级会被覆盖，且无法在多机间保持一致。
4. 升级重跑 `install.sh` 只替换程序；已有且不含 `CHANGE_ME` 的 `/etc/default/taskrunner` 会保留并自动重启服务。

约定建议：各机 PG 监听 `127.0.0.1:5432`、库/用户 `taskrunner`，仅密码不同。文件里若仍含 `CHANGE_ME`，systemd 的 `ExecStartPre` 会拒绝启动，避免带着占位符跑起来。

## 日志

| 位置 | 内容 |
|------|------|
| `journalctl -u taskrunner-api -f` / `-u taskrunner-worker -f` | 控制台输出 |
| `/opt/taskrunner/api/logs/`、`/opt/taskrunner/worker/logs/` | Serilog 文件日志，按天滚动，保留 30 天 |

> 与 Windows 的差别：Linux 下 Api/Worker 各写各的 logs 目录（同目录跨进程共享文件日志是 Windows 专属行为）。

## 反向代理（可选，推荐）

Api 默认只绑 `127.0.0.1`，公网访问走 nginx：

```nginx
server {
    listen 80;
    server_name tasks.example.com;

    location / {
        proxy_pass         http://127.0.0.1:5088;
        proxy_set_header   Host $host;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
    }
}
```

## 升级与回滚

```bash
# 升级：打新包 → 上传 → 重跑安装脚本（自动停服、替换、按需重启）
sudo bash /tmp/taskrunner/deploy/install.sh

# 回滚：用旧版本包重新执行同样的安装脚本即可（数据库 schema 向前兼容，无需回退）
```

## 常见问题

- **Api 启动即失败，日志提示管理员密钥**：`/etc/default/taskrunner` 里 `Hangfire__AdminApiKey` 还没填（3.1.0 起 fail-fast）。填好后 `sudo systemctl restart taskrunner-api`。
- **`systemctl start` 立刻失败，journal 提到 CHANGE_ME / ExecStartPre**：`/etc/default/taskrunner` 仍含占位符。只改该文件两处 `CHANGE_ME`，**不要**去改 `/opt/taskrunner/**/appsettings.json`，然后 `sudo systemctl restart taskrunner-api taskrunner-worker`。
- **`systemctl start` 超时后服务标记 failed**：包是旧版产物、不支持 `Type=notify`。用 `sudo TASKRUNNER_SERVICE_TYPE=simple bash ./deploy/install.sh` 重装，或换新包。
- **数据库连不上**：确认 `/etc/default/taskrunner` 里连接串、PG 的 `pg_hba.conf`/监听地址；本机未装 PG 时 `postgresql.service` 的排序不生效，属正常。
- **改了配置没生效**：EnvironmentFile 只在服务启动时读取，改完必须 `systemctl restart`。若误改了包内 `appsettings.json`，下次升级会被覆盖——应改回本机 env。
- **首次部署 Worker 启动报迁移冲突**：极小概率（Api/Worker 同时首启都在跑迁移），重试一次即自愈；Worker 单元已排序在 Api 之后，正常不会再出现。
