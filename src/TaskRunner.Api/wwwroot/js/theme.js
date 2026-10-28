(function () {
    const toggle = document.getElementById("theme-toggle");
    if (!toggle) return;
    const syncIcon = () => {
        toggle.textContent = document.documentElement.getAttribute("data-theme") === "dark" ? "☀" : "☾";
    };
    toggle.addEventListener("click", () => {
        const next = document.documentElement.getAttribute("data-theme") === "dark" ? "light" : "dark";
        document.documentElement.setAttribute("data-theme", next);
        localStorage.setItem("tr-theme", next);
        syncIcon();
    });
    syncIcon();
})();
