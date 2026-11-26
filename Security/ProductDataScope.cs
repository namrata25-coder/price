using CrmLeadManagement.Models;

namespace CrmLeadManagement.Security;

public static class ProductDataScope
{
    public static IQueryable<Product> Products(AppDbContext db, UserRoleEntry? user)
    {
        var query = db.Products.AsQueryable();
        if (user == null) return query.Where(_ => false);
        if (PermissionChecker.IsSuperAdmin(user)) return query;

        var effectiveAdminId = user.GetEffectiveAdminId();
        return effectiveAdminId.HasValue
            ? query.Where(p => p.AdminId == effectiveAdminId.Value)
            : query.Where(_ => false);
    }
}
