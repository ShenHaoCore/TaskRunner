using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TaskRunner.Api.Controllers;

/// <summary>运维静态页（MVC）：首页 / 登录 / 运行台。</summary>
[AllowAnonymous]
public sealed class HomeController : Controller
{
    [HttpGet("/")]
    public IActionResult Index()
    {
        ViewData["Title"] = "TaskRunner — 任务调度平台";
        ViewData["NavActive"] = "home";
        ViewData["ShowRoleBadge"] = true;
        return View();
    }

    [HttpGet("/login")]
    public IActionResult Login()
    {
        ViewData["Title"] = "登录 — TaskRunner";
        ViewData["NavActive"] = "login";
        ViewData["MinimalNav"] = true;
        return View();
    }

    [HttpGet("/run")]
    public IActionResult Run()
    {
        ViewData["Title"] = "任务运行台 — TaskRunner";
        ViewData["NavActive"] = "run";
        return View();
    }
}
