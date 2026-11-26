namespace CrmLeadManagement.Models;

public class Product
{
    public int Id { get; set; }
    public int? AdminId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Features { get; set; } = "";
    public decimal Price { get; set; }
    public string? Url { get; set; }
}
