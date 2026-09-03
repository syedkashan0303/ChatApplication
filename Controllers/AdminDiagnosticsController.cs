using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SignalRMVC.Controllers;

[Authorize(Roles = "Manager")]
[Route("Admin/Diagnostics")]
public sealed class AdminDiagnosticsController : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Index()
    {
        return View("~/Views/AdminDiagnostics/Index.cshtml");
    }
}
