using System.Security.Claims;
using CrmLeadManagement.Models;

namespace CrmLeadManagement.Security;

/// <summary>
/// Scopes leads according to SaaS multi-tenancy and role assignments.
/// Super Admin: global platform authority (can view all leads or filter by Admin).
/// Admin: strictly scoped to leads belonging to their own AdminId.
/// Team Members: strictly scoped to their Admin's leads, restricted to assigned leads unless role has DataAccess="All".
/// </summary>
public static class LeadDataScope
{
    public static bool IsSuperAdmin(AppDbContext db, ClaimsPrincipal? principal)
    {
        var user = PermissionChecker.GetCurrentUser(db, principal);
        return PermissionChecker.IsSuperAdmin(principal, user);
    }

    public static bool HasFullAccess(AppDbContext db, ClaimsPrincipal? principal)
    {
        var user = PermissionChecker.GetCurrentUser(db, principal);
        return PermissionChecker.IsAdmin(principal, user);
    }

    public static bool IsAssignedToUser(Lead lead, UserRoleEntry? user) =>
        user != null &&
        (string.Equals(lead.AssignedTo, user.Name, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(lead.AssignedTo, user.Email, StringComparison.OrdinalIgnoreCase));

    public static IQueryable<Lead> ScopedLeadsQuery(AppDbContext db, ClaimsPrincipal? principal, int? filterAdminId = null)
    {
        var query = db.Leads.AsQueryable();
        var user = PermissionChecker.GetCurrentUser(db, principal);
        if (user == null && principal?.Identity?.IsAuthenticated != true)
        {
            return query.Where(_ => false);
        }

        // 1. Super Admin: global access across all tenants
        if (PermissionChecker.IsSuperAdmin(principal, user))
        {
            if (filterAdminId.HasValue && filterAdminId.Value > 0)
            {
                return query.Where(l => l.AdminId == filterAdminId.Value);
            }
            return query;
        }

        // 2. Tenant isolation by AdminId
        var effectiveAdminId = user?.GetEffectiveAdminId();
        if (!effectiveAdminId.HasValue)
        {
            return query.Where(_ => false);
        }

        query = query.Where(l => l.AdminId == effectiveAdminId.Value);

        // If Admin: full access within their own tenant
        if (string.Equals(user?.Role, "Admin", StringComparison.OrdinalIgnoreCase) || principal?.IsInRole("Admin") == true)
        {
            return query;
        }

        // 3. Regular staff within tenant: check role DataAccess setting
        var roleMaster = !string.IsNullOrWhiteSpace(user?.Role)
            ? db.RoleMasters.FirstOrDefault(r => r.RoleName == user!.Role)
            : null;

        if (roleMaster != null && string.Equals(roleMaster.DataAccess, "All", StringComparison.OrdinalIgnoreCase))
        {
            return query;
        }

        var userName = user?.Name ?? "";
        var userEmail = user?.Email ?? "";
        return query.Where(l =>
            (!string.IsNullOrEmpty(userName) && l.AssignedTo != null && l.AssignedTo.ToLower() == userName.ToLower()) ||
            (!string.IsNullOrEmpty(userEmail) && l.AssignedTo != null && l.AssignedTo.ToLower() == userEmail.ToLower()));
    }

    public static IEnumerable<Lead> VisibleLeads(AppDbContext db, ClaimsPrincipal? principal, int? filterAdminId = null)
    {
        return ScopedLeadsQuery(db, principal, filterAdminId).ToList();
    }

    public static Lead? GetVisibleLead(AppDbContext db, ClaimsPrincipal? principal, string leadId)
    {
        if (string.IsNullOrWhiteSpace(leadId)) return null;
        return ScopedLeadsQuery(db, principal).FirstOrDefault(l => l.LeadId == leadId);
    }
}
