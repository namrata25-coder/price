using CrmLeadManagement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers;

public class PublicLandingPageViewModel
{
    public CmsLandingPageConfig Config { get; set; } = new();
    public List<SaaSSubscriptionPlan> Plans { get; set; } = new();
    public List<CmsFaqArticle> Faqs { get; set; } = new();
    public SaaSPlatformSettings Settings { get; set; } = new();
}

public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("")]
    [HttpGet("landing")]
    [HttpGet("Home/Index")]
    public async Task<IActionResult> Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole("Super Admin"))
            {
                return RedirectToAction("Dashboard", "SuperAdmin");
            }
            return RedirectToAction("List", "Lead");
        }

        var config = await _db.LandingPageConfigs.FirstOrDefaultAsync() ?? new CmsLandingPageConfig();
        var plans = await _db.SubscriptionPlans
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync();

        var faqs = await _db.FaqArticles
            .Where(f => f.IsActive)
            .OrderBy(f => f.DisplayOrder)
            .ToListAsync();

        var settings = await _db.PlatformSettings.FirstOrDefaultAsync() ?? new SaaSPlatformSettings();

        var vm = new PublicLandingPageViewModel
        {
            Config = config,
            Plans = plans,
            Faqs = faqs,
            Settings = settings
        };

        return View("Landing", vm);
    }

    public IActionResult Error()
    {
        return Problem();
    }
}
