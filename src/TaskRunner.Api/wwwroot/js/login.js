(function () {
    const form = document.getElementById("login-form");
    const input = document.getElementById("api-key");
    const button = document.getElementById("submit");
    const error = document.getElementById("error");

    function showError(text) {
        error.textContent = text;
        error.hidden = !text;
    }

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const apiKey = input.value.trim();
        if (!apiKey) {
            showError("请输入管理员密钥。");
            return;
        }
        button.disabled = true;
        button.textContent = "登录中…";
        showError("");
        try {
            const res = await fetch("/api/auth/login", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ apiKey })
            });
            if (res.ok) {
                location.href = "/taskrunner";
                return;
            }
            showError(res.status === 401 ? "密钥无效，请核对后重试。" : "登录失败（HTTP " + res.status + "）。");
        } catch (err) {
            showError("网络错误：" + (err.message || "未知异常"));
        } finally {
            button.disabled = false;
            button.textContent = "登录并打开 Dashboard";
        }
    });
})();
