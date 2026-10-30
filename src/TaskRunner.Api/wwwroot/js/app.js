// TaskRunner 页面交互：防重复提交、本地时间渲染、提示条自动消失。
// 主题初始化在 _Layout 内联（须先于样式应用，防 FOUC），不在此文件。

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

// 服务端输出 UTC ISO 时间，按浏览器时区渲染
document.querySelectorAll("[data-local-time]").forEach(function (el) {
    var raw = el.getAttribute("datetime");
    if (!raw) { return; }
    var normalized = /[zZ]$|[+-]\d\d:?\d\d$/.test(raw) ? raw : raw + "Z";
    el.textContent = new Date(normalized).toLocaleString();
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
