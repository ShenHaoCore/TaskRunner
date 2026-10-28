(async function () {
    const hint = document.getElementById("hint");
    const scalar = document.getElementById("link-scalar");
    const dashboard = document.getElementById("link-dashboard");
    const roleText = document.getElementById("role-text");
    const workerText = document.getElementById("worker-text");
    const serversBox = document.getElementById("metric-servers");

    try {
        const info = await fetch("/info").then((r) => r.json());
        if (info.dashboard) {
            dashboard.href = info.dashboard;
            const entry = document.getElementById("link-dashboard-entry");
            if (entry) entry.href = info.dashboard;
        }
        const versionEl = document.getElementById("version");
        if (versionEl && info.version) versionEl.textContent = " v" + info.version;
        if (roleText) {
            const parts = [info.environment, info.role].filter(Boolean);
            if (parts.length) roleText.textContent = parts.join(" · ") + (info.readOnly ? " · 只读" : "");
        }
        if (info.scalar) {
            scalar.hidden = false;
            scalar.href = info.scalar;
        }
    } catch (_) {
        /* keep defaults */
    }

    try {
        const stats = await fetch("/api/tasks/statistics").then((r) => {
            if (!r.ok) throw new Error(r.status === 401 ? "401" : r.statusText);
            return r.json();
        });
        for (const key of ["succeeded", "failed", "enqueued", "processing", "recurring", "servers"]) {
            const el = document.querySelector('[data-k="' + key + '"]');
            if (el) el.textContent = stats[key] ?? "—";
        }
        const online = (stats.servers ?? 0) > 0;
        serversBox.className = "metric " + (online ? "metric-success" : "metric-danger");
        if (workerText) workerText.textContent = online ? "Worker 在线 ×" + stats.servers : "无 Worker";
        hint.textContent = "数据来自 Hangfire 监控接口。";
    } catch (err) {
        if (err.message === "401") {
            hint.innerHTML = '统计需要管理员鉴权：<a href="/login">先登录</a>，再刷新本页。';
            if (workerText) workerText.textContent = "需要鉴权";
        } else {
            hint.textContent = "统计暂不可用：" + (err.message || "未知错误");
            if (workerText) workerText.textContent = "不可用";
            serversBox.className = "metric metric-default";
        }
    }
})();
