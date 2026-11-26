using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers;

[Authorize]
public class ProductController : Controller
{
    private const int DefaultPageSize = 10;
    private readonly AppDbContext _db;

    public ProductController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        if (!HasPermission("View")) return AccessDenied();

        page = Math.Max(page, 1);
        var query = ProductDataScope.Products(_db, CurrentUser()).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.Name.Contains(term) ||
                                     p.Description.Contains(term) ||
                                     p.Features.Contains(term));
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)DefaultPageSize);
        page = totalPages == 0 ? 1 : Math.Min(page, totalPages);

        ViewData["Title"] = "Product Management";
        ViewData["ActivePage"] = "PortfolioProduct";
        ViewData["CanCreateProduct"] = HasPermission("Create");
        ViewData["CanEditProduct"] = HasPermission("Edit");
        ViewData["CanDeleteProduct"] = HasPermission("Delete");
        return View(new ProductListViewModel
        {
            Products = await query.OrderByDescending(p => p.Id)
                .Skip((page - 1) * DefaultPageSize)
                .Take(DefaultPageSize)
                .ToListAsync(),
            Search = search?.Trim() ?? "",
            Page = page,
            PageSize = DefaultPageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        });
    }

    [HttpGet]
    public IActionResult Create()
    {
        if (!HasPermission("Create")) return AccessDenied();
        SetPageState();
        return View(new ProductInputModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductInputModel input)
    {
        if (!HasPermission("Create")) return AccessDenied();
        if (!ModelState.IsValid)
        {
            SetPageState();
            return View(input);
        }

        var user = CurrentUser();
        _db.Products.Add(new Product
        {
            AdminId = PermissionChecker.IsSuperAdmin(user) ? null : user?.GetEffectiveAdminId(),
            Name = input.Name.Trim(),
            Description = input.Description.Trim(),
            Features = input.Features.Trim(),
            Price = input.Price,
            Url = NormalizeUrl(input.Url)
        });
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "Product added successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (!HasPermission("View")) return AccessDenied();
        var product = await FindProduct(id);
        if (product == null) return ProductNotFound();

        SetPageState();
        return View(product);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (!HasPermission("Edit")) return AccessDenied();
        var product = await FindProduct(id);
        if (product == null) return ProductNotFound();

        SetPageState();
        ViewData["ProductId"] = id;
        return View(new ProductInputModel
        {
            Name = product.Name,
            Description = product.Description,
            Features = product.Features,
            Price = product.Price,
            Url = product.Url
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductInputModel input)
    {
        if (!HasPermission("Edit")) return AccessDenied();
        var product = await FindProduct(id);
        if (product == null) return ProductNotFound();

        if (!ModelState.IsValid)
        {
            SetPageState();
            ViewData["ProductId"] = id;
            return View(input);
        }

        product.Name = input.Name.Trim();
        product.Description = input.Description.Trim();
        product.Features = input.Features.Trim();
        product.Price = input.Price;
        product.Url = NormalizeUrl(input.Url);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "Product updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!HasPermission("Delete")) return AccessDenied();
        var product = await FindProduct(id);
        if (product == null) return ProductNotFound();

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Product deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    private UserRoleEntry? CurrentUser() => PermissionChecker.GetCurrentUser(_db, User);

    private bool HasPermission(string action) =>
        PermissionChecker.CanPerformAction(_db, User, "Product", action);

    private async Task<Product?> FindProduct(int id) =>
        await ProductDataScope.Products(_db, CurrentUser()).FirstOrDefaultAsync(p => p.Id == id);

    private IActionResult AccessDenied()
    {
        TempData["ErrorMessage"] = "Access Denied: You do not have permission to manage Products.";
        return RedirectToAction("List", "Lead");
    }

    private IActionResult ProductNotFound()
    {
        TempData["ErrorMessage"] = "Product not found or you do not have access to it.";
        return RedirectToAction(nameof(Index));
    }

    private void SetPageState()
    {
        ViewData["Title"] = "Product Management";
        ViewData["ActivePage"] = "PortfolioProduct";
    }

    private static string? NormalizeUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ? null : url.Trim();
}
