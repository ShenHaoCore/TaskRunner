using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace TaskRunner.Api.Controllers;

/// <summary>页面操作结果的一次性提示（随重定向展示）。</summary>
public static class TempDataFlashExtensions
{
    public static void SetFlashSuccess(this ITempDataDictionary tempData, string message) =>
        tempData["Flash"] = message;

    public static void SetFlashError(this ITempDataDictionary tempData, string message) =>
        tempData["FlashError"] = message;
}
