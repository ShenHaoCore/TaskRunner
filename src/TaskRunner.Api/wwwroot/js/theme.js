(function () {
    const toggle = document.getElementById("theme-toggle");
    if (!toggle) return;
    const syncIcon = () => {
        var dark = document.documentElement.getAttribute("data-theme") === "dark";
        toggle.textContent = dark ? "☀" : "☾";
        toggle.setAttribute("aria-pressed", String(!dark));
    };
    toggle.addEventListener("click", () => {
        const next = document.documentElement.getAttribute("data-theme") === "dark" ? "light" : "dark";
        document.documentElement.setAttribute("data-theme", next);
        localStorage.setItem("tr-theme", next);
        syncIcon();
    });
    syncIcon();
})();
