IF DB_ID(N'TaskRunner') IS NULL
    CREATE DATABASE TaskRunner;
GO

IF DB_ID(N'TaskRunner_Hangfire') IS NULL
    CREATE DATABASE TaskRunner_Hangfire;
GO

-- 业务表（TaskConfigs / SyncWindows / RuntimeSettings）由 Api/Worker 启动时 EF 迁移创建。
-- 不要与迁移同时手工建表。
