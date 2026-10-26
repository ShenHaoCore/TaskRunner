# Changelog

本项目的所有显著变更都记录在本文件。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循[语义化版本](https://semver.org/lang/zh-CN/)。

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
