using Microsoft.AspNetCore.Mvc;

namespace ReRhythm.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Education()
    {
        return View();
    }
}
