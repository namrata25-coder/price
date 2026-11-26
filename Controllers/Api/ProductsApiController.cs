using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos.App;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.Api;

[Route("api/products")]
[Route("api/app/v1/products")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class ProductsApiController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProductsApiController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search)
    {
        if (!CanPerform("View")) return Forbidden("You do not have permission to view products.");

        var query = ProductDataScope.Products(_db, CurrentUser()).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.Name.Contains(term) ||
                                     p.Description.Contains(term) ||
                                     p.Features.Contains(term));
        }

        var products = await query.OrderByDescending(p => p.Id)
            .Select(p => new ProductDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Features = p.Features,
                Price = p.Price,
                Url = p.Url
            })
            .ToListAsync();
        return Ok(AppApiResponse<IReadOnlyList<ProductDto>>.Ok(products, "Products retrieved successfully."));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!CanPerform("View")) return Forbidden("You do not have permission to view products.");
        var product = await ProductDataScope.Products(_db, CurrentUser())
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound(AppApiErrorResponse.Fail("Product not found."));

        return Ok(AppApiResponse<ProductDto>.Ok(ToDto(product), "Product retrieved successfully."));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProductInputModel input)
    {
        if (!CanPerform("Create")) return Forbidden("You do not have permission to create products.");
        if (input == null || !TryValidateModel(input)) return ValidationFailure();

        var user = CurrentUser();
        var product = new Product
        {
            AdminId = PermissionChecker.IsSuperAdmin(user) ? null : user?.GetEffectiveAdminId(),
            Name = input.Name.Trim(),
            Description = input.Description.Trim(),
            Features = input.Features.Trim(),
            Price = input.Price,
            Url = NormalizeUrl(input.Url)
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = product.Id },
            AppApiResponse<ProductDto>.Ok(ToDto(product), "Product created successfully."));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ProductInputModel input)
    {
        if (!CanPerform("Edit")) return Forbidden("You do not have permission to update products.");
        if (input == null || !TryValidateModel(input)) return ValidationFailure();

        var product = await ProductDataScope.Products(_db, CurrentUser())
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound(AppApiErrorResponse.Fail("Product not found."));

        product.Name = input.Name.Trim();
        product.Description = input.Description.Trim();
        product.Features = input.Features.Trim();
        product.Price = input.Price;
        product.Url = NormalizeUrl(input.Url);
        await _db.SaveChangesAsync();

        return Ok(AppApiResponse<ProductDto>.Ok(ToDto(product), "Product updated successfully."));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!CanPerform("Delete")) return Forbidden("You do not have permission to delete products.");
        var product = await ProductDataScope.Products(_db, CurrentUser())
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound(AppApiErrorResponse.Fail("Product not found."));

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();
        return Ok(AppApiResponse.Ok("Product deleted successfully."));
    }

    private UserRoleEntry? CurrentUser() => PermissionChecker.GetCurrentUser(_db, User);

    private bool CanPerform(string action) =>
        PermissionChecker.CanPerformAction(_db, User, "Product", action);

    private IActionResult Forbidden(string message) =>
        StatusCode(StatusCodes.Status403Forbidden, AppApiErrorResponse.Fail(message));

    private IActionResult ValidationFailure()
    {
        var errors = ModelState
            .Where(item => item.Value?.Errors.Count > 0)
            .ToDictionary(
                item => item.Key,
                item => item.Value!.Errors.Select(error => error.ErrorMessage).ToArray());
        return BadRequest(AppApiErrorResponse.Fail("Product validation failed.", errors));
    }

    private static ProductDto ToDto(Product product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        Description = product.Description,
        Features = product.Features,
        Price = product.Price,
        Url = product.Url
    };

    private static string? NormalizeUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ? null : url.Trim();
}

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Features { get; set; } = "";
    public decimal Price { get; set; }
    public string? Url { get; set; }
}
