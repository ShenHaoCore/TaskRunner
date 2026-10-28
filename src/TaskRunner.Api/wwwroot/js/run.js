(function () {
    const rowsEl = document.getElementById("rows");
    const emptyEl = document.getElementById("empty");
    const filterEl = document.getElementById("filter");
    const banner = document.getElementById("banner");
    let tasks = [];

    function showBanner(text, kind) {
        banner.textContent = text;
        banner.className = "alert " + (kind === "error" ? "alert-danger" : "alert-success");
        banner.hidden = false;
    }

    function clearBanner() {
        banner.className = "alert";
        banner.textContent = "";
        banner.hidden = true;
    }

    function render() {
        const q = filterEl.value.trim().toLowerCase();
        const filtered = tasks.filter((t) => {
            if (!q) return true;
            return (t.jobId || "").toLowerCase().includes(q)
                || (t.jobName || "").toLowerCase().includes(q);
        });
        rowsEl.innerHTML = "";
        emptyEl.hidden = filtered.length > 0;
        for (const t of filtered) {
            const tr = document.createElement("tr");
            tr.innerHTML =
                "<td><strong>" + escapeHtml(t.jobName || t.jobId) + "</strong>"
                + "<div class=\"text-muted word-break\"><small>" + escapeHtml(t.jobId) + "</small></div></td>"
                + "<td class=\"hide-sm text-muted\">" + escapeHtml(t.queue || "default") + "</td>"
                + "<td><span class=\"label " + (t.isEnabled ? "label-success" : "label-default") + "\">"
                + (t.isEnabled ? "启用" : "停用") + "</span></td>"
                + "<td class=\"align-right\"></td>";
            const btn = document.createElement("button");
            btn.className = "btn btn-success btn-sm";
            btn.type = "button";
            btn.textContent = "立即运行";
            btn.addEventListener("click", () => trigger(t.jobId, btn));
            tr.lastElementChild.appendChild(btn);
            rowsEl.appendChild(tr);
        }
    }

    function escapeHtml(value) {
        return String(value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    }

    async function load() {
        clearBanner();
        const res = await fetch("/api/tasks", { credentials: "same-origin" });
        if (res.status === 401) {
            showBanner("需要管理员登录后才能查看任务。", "error");
            tasks = [];
            render();
            return;
        }
        if (!res.ok) {
            showBanner("加载失败（HTTP " + res.status + "）。", "error");
            return;
        }
        tasks = await res.json();
        render();
    }

    async function trigger(jobId, button) {
        button.disabled = true;
        clearBanner();
        try {
            const res = await fetch("/api/tasks/" + encodeURIComponent(jobId) + "/trigger", {
                method: "POST",
                credentials: "same-origin"
            });
            if (res.status === 401) {
                showBanner("会话已失效，请重新登录。", "error");
                return;
            }
            const body = await res.json().catch(() => ({}));
            if (!res.ok) {
                showBanner(body.message || ("触发失败（HTTP " + res.status + "）"), "error");
                return;
            }
            showBanner("已入队 " + jobId + " → " + (body.queueJobId || ""), "ok");
        } catch (err) {
            showBanner("网络错误：" + (err.message || "未知异常"), "error");
        } finally {
            button.disabled = false;
        }
    }

    filterEl.addEventListener("input", render);
    document.getElementById("reload").addEventListener("click", load);
    load();
})();
