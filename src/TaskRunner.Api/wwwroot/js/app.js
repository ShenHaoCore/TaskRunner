// TaskRunner 页面交互：防重复提交、本地时间渲染、提示条自动消失、
// 可见性感知自动刷新、运行台批量操作 / 行内改 Cron / 详情展开。
// 主题初始化在 _Layout 内联（须先于样式应用，防 FOUC），不在此文件。

// 服务端输出 UTC ISO 时间，按浏览器时区渲染；root 支持动态插入内容后局部重渲染
function refreshLocalTimes(root) {
    (root || document).querySelectorAll("[data-local-time]").forEach(function (el) {
        var raw = el.getAttribute("datetime");
        if (!raw) { return; }
        var normalized = /[zZ]$|[+-]\d\d:?\d\d$/.test(raw) ? raw : raw + "Z";
        el.textContent = new Date(normalized).toLocaleString();
    });
}
refreshLocalTimes();

// 表单提交后禁用提交按钮直到页面跳转，防双击重复触发
//（PRG 重定向回来是新页面，按钮自然恢复；此前 3 秒定时恢复在慢网络下可重复入队）
document.querySelectorAll("form[data-submit-once]").forEach(function (form) {
    form.addEventListener("submit", function () {
        var button = form.querySelector("button[type=submit]");
        if (!button || button.disabled) { return; }
        button.disabled = true;
        button.setAttribute("aria-busy", "true");
    });
});

// 成功提示 5 秒自动淡出：悬停暂停、移出恢复；页面不可见时暂停
//（此前后台标签页照常倒计时，切回来消息已消失；hover 暂停后移出也不再恢复）
(function () {
    var VISIBLE_MS = 5000, LEAVING_MS = 300;

    function dismiss(el) {
        el.classList.add("flash-leaving");
        setTimeout(function () { el.remove(); }, LEAVING_MS);
    }

    document.querySelectorAll(".flash[data-auto-dismiss='true']").forEach(function (el) {
        var remaining = VISIBLE_MS;
        var startedAt = 0;
        var timer = 0;
        var hovered = false;

        function pause() {
            if (!timer) { return; }
            clearTimeout(timer);
            timer = 0;
            remaining = Math.max(0, remaining - (Date.now() - startedAt));
            if (remaining <= 0) { dismiss(el); }
        }

        function resume() {
            if (timer || remaining <= 0 || el.classList.contains("flash-leaving")) { return; }
            startedAt = Date.now();
            timer = setTimeout(function () { timer = 0; dismiss(el); }, remaining);
        }

        el.addEventListener("mouseenter", function () { hovered = true; pause(); });
        el.addEventListener("mouseleave", function () { hovered = false; resume(); });
        document.addEventListener("visibilitychange", function () {
            (document.hidden || hovered) ? pause() : resume();
        });

        // 后台打开的标签页不提前倒计时，首次可见才计时
        if (!document.hidden) { resume(); }
    });
})();

// 可见性感知自动刷新：main[data-auto-refresh="秒"]。
// 页面隐藏时暂停；回前台距上次刷新超时则整页刷新；
// 有打开的 Cron 编辑器或展开的详情行时跳过本轮（不打断输入与阅读）。
(function () {
    var main = document.querySelector("main[data-auto-refresh]");
    if (!main) { return; }
    var seconds = parseInt(main.getAttribute("data-auto-refresh"), 10) || 30;
    var lastReload = Date.now();
    var hiddenAt = 0;

    function interactionOpen() {
        return document.querySelector(".cron-editor:not([hidden])") !== null
            || document.querySelector(".detail-row:not([hidden])") !== null;
    }

    setInterval(function () {
        if (document.hidden || interactionOpen()) { return; }
        if (Date.now() - lastReload < seconds * 1000) { return; }
        location.reload();
    }, 5000);

    document.addEventListener("visibilitychange", function () {
        if (document.hidden) {
            hiddenAt = Date.now();
        } else if (hiddenAt && Date.now() - hiddenAt >= seconds * 1000 && !interactionOpen()) {
            location.reload();
        } else {
            lastReload = Date.now();
            hiddenAt = 0;
        }
    });
})();

// 运行台批量操作：勾选在表格外（避免与行内触发表单嵌套），提交前写入隐藏域
(function () {
    var form = document.getElementById("bulk-form");
    if (!form) { return; }
    var checks = Array.prototype.slice.call(document.querySelectorAll(".bulk-check"));
    var buttons = form.querySelectorAll("button[name=action]");
    var count = document.getElementById("bulk-count");
    var all = document.getElementById("bulk-all");

    function sync() {
        var selected = checks.filter(function (c) { return c.checked; }).length;
        buttons.forEach(function (b) { b.disabled = selected === 0; });
        if (count) { count.textContent = String(selected); }
        if (all) { all.checked = selected > 0 && selected === checks.length; }
    }

    if (all) {
        all.addEventListener("change", function () {
            checks.forEach(function (c) { c.checked = all.checked; });
            sync();
        });
    }
    checks.forEach(function (c) { c.addEventListener("change", sync); });

    form.addEventListener("submit", function () {
        form.querySelectorAll("input[name='jobIds']").forEach(function (input) { input.remove(); });
        checks.filter(function (c) { return c.checked; }).forEach(function (c) {
            var input = document.createElement("input");
            input.type = "hidden";
            input.name = "jobIds";
            input.value = c.value;
            form.appendChild(input);
        });
    });

    sync();
})();

// 运行台行内改 Cron：✎ 展开预渲染表单；输入防抖 300ms 调 /api/tasks/cron-preview 预览下次执行
(function () {
    function setOpen(jobId, open) {
        var row = document.getElementById("cron-row-" + jobId);
        if (!row) { return; }
        row.hidden = !open;
        var toggle = document.querySelector('[data-cron-toggle="' + jobId + '"]');
        if (toggle) { toggle.setAttribute("aria-expanded", String(open)); }
        if (open) {
            var input = row.querySelector("input[name=cron]");
            if (input) { input.focus(); }
        }
    }

    document.querySelectorAll("[data-cron-toggle]").forEach(function (btn) {
        var jobId = btn.getAttribute("data-cron-toggle");
        btn.addEventListener("click", function () {
            var row = document.getElementById("cron-row-" + jobId);
            setOpen(jobId, !row || row.hidden);
        });
    });

    document.querySelectorAll("[data-cron-editor]").forEach(function (form) {
        var jobId = form.getAttribute("data-cron-editor");
        var input = form.querySelector("input[name=cron]");
        var preview = form.querySelector(".cron-preview");
        var timer = 0;

        form.querySelector("[data-cron-cancel]").addEventListener("click", function () {
            setOpen(jobId, false);
        });

        input.addEventListener("input", function () {
            clearTimeout(timer);
            var expr = input.value.trim();
            if (!expr) { preview.textContent = ""; return; }
            timer = setTimeout(function () {
                fetch("/api/tasks/cron-preview?expr=" + encodeURIComponent(expr), {
                    headers: { Accept: "application/json" }
                })
                    .then(function (response) { return response.ok ? response.json() : Promise.reject(); })
                    .then(function (data) {
                        var items = (data && data.occurrences) || [];
                        if (!items.length) {
                            preview.textContent = "该表达式没有更多执行时间";
                            return;
                        }
                        preview.textContent = "下次执行：";
                        items.forEach(function (iso) {
                            var time = document.createElement("time");
                            time.setAttribute("datetime", iso);
                            preview.appendChild(time);
                        });
                        refreshLocalTimes(preview);
                    })
                    .catch(function () {
                        preview.textContent = "无法预览（表达式无效或服务不可用）";
                    });
            }, 300);
        });
    });
})();

// 运行台任务行详情：fetch REST 历史（同源 Cookie 鉴权），展示最近 5 次 Succeeded/Failed 结局
(function () {
    document.querySelectorAll("[data-detail-toggle]").forEach(function (btn) {
        var jobId = btn.getAttribute("data-detail-toggle");
        var row = document.getElementById("detail-row-" + jobId);
        if (!row) { return; }
        var panel = row.querySelector(".detail-panel");

        btn.addEventListener("click", function () {
            var open = !row.hidden;
            row.hidden = open;
            btn.setAttribute("aria-expanded", String(!open));
            if (open) { return; }

            panel.textContent = "加载中…";
            fetch("/api/tasks/" + encodeURIComponent(jobId) + "/history?limit=10", {
                headers: { Accept: "application/json" }
            })
                .then(function (response) { return response.ok ? response.json() : Promise.reject(); })
                .then(function (items) {
                    var outcomes = (items || [])
                        .filter(function (item) { return item.state === "Succeeded" || item.state === "Failed"; })
                        .slice(0, 5);
                    if (!outcomes.length) { panel.textContent = "暂无执行记录。"; return; }

                    panel.textContent = "";
                    outcomes.forEach(function (item) {
                        var line = document.createElement("div");
                        line.className = "detail-line";
                        var dot = document.createElement("span");
                        dot.className = "state-dot " + (item.state === "Succeeded" ? "ok" : "bad");
                        line.appendChild(dot);
                        var time = document.createElement("time");
                        time.setAttribute("datetime", item.occurredAt || "");
                        time.textContent = item.occurredAt ? "" : "—";
                        line.appendChild(time);
                        if (item.reason) {
                            var reason = document.createElement("span");
                            reason.className = "muted";
                            reason.textContent = item.reason;
                            line.appendChild(reason);
                        }
                        panel.appendChild(line);
                    });
                    refreshLocalTimes(panel);
                })
                .catch(function () {
                    panel.textContent = "加载失败 — 请稍后重试或到 Dashboard 查看。";
                });
        });
    });
})();
