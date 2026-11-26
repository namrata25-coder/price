namespace CrmLeadManagement.Models;

public class ProductListViewModel
{
    public IReadOnlyList<Product> Products { get; set; } = Array.Empty<Product>();
    public string Search { get; set; } = "";
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}
