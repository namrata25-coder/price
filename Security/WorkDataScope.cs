using CrmLeadManagement.Models;

namespace CrmLeadManagement.Security;

public static class WorkDataScope
{
    public static bool IsSuperAdmin(UserRoleEntry? user) =>
        user != null && string.Equals(user.Role, "Super Admin", StringComparison.OrdinalIgnoreCase);

    public static bool IsAdmin(UserRoleEntry? user) =>
        user != null && string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase);

    public static bool HasFullAccess(UserRoleEntry? user) =>
        IsSuperAdmin(user) || IsAdmin(user);

    public static IQueryable<WorkProject> Projects(AppDbContext db, UserRoleEntry? user, int? filterAdminId = null)
    {
        var query = db.WorkProjects.AsQueryable();
        if (user == null) return query.Where(_ => false);

        // 1. Super Admin: full access across all tenants (can filter by AdminId if supplied)
        if (IsSuperAdmin(user))
        {
            if (filterAdminId.HasValue && filterAdminId.Value > 0)
            {
                return query.Where(p => p.AdminId == filterAdminId.Value);
            }
            return query;
        }

        // 2. Strict tenant isolation by AdminId
        var effectiveAdminId = user.GetEffectiveAdminId();
        if (!effectiveAdminId.HasValue) return query.Where(_ => false);

        query = query.Where(p => p.AdminId == effectiveAdminId.Value);

        // If Admin: full access to all projects in their tenant
        if (IsAdmin(user)) return query;

        // Check if role has DataAccess == "All"
        var roleMaster = !string.IsNullOrWhiteSpace(user.Role) ? db.RoleMasters.FirstOrDefault(r => r.RoleName == user.Role) : null;
        if (roleMaster != null && string.Equals(roleMaster.DataAccess, "All", StringComparison.OrdinalIgnoreCase))
        {
            return query;
        }

        var name = (user.Name ?? "").Trim().ToLower();
        var email = (user.Email ?? "").Trim().ToLower();
        var hasName = name.Length > 0;
        var hasEmail = email.Length > 0;
        return query.Where(p =>
            (hasName &&
             ((p.ProjectManager != null && p.ProjectManager.Trim().ToLower() == name) ||
              (p.AssignedMembers != null &&
               ("," + p.AssignedMembers.ToLower().Replace(", ", ",").Replace(" ,", ",") + ",").Contains("," + name + ",")))) ||
            (hasEmail &&
             ((p.ProjectManager != null && p.ProjectManager.Trim().ToLower() == email) ||
              (p.AssignedMembers != null &&
               ("," + p.AssignedMembers.ToLower().Replace(", ", ",").Replace(" ,", ",") + ",").Contains("," + email + ",")))));
    }

    public static IQueryable<WorkMilestone> Milestones(AppDbContext db, UserRoleEntry? user, int? filterAdminId = null)
    {
        var projects = Projects(db, user, filterAdminId);
        var query = db.WorkMilestones.Where(m => projects.Any(p => p.Id == m.ProjectId));
        if (user == null) return query.Where(_ => false);
        if (HasFullAccess(user)) return query;

        var name = (user.Name ?? "").Trim().ToLower();
        var email = (user.Email ?? "").Trim().ToLower();
        var hasName = name.Length > 0;
        var hasEmail = email.Length > 0;
        return query.Where(m =>
            (hasName && m.AssignedMembers != null &&
             ("," + m.AssignedMembers.ToLower().Replace(", ", ",").Replace(" ,", ",") + ",").Contains("," + name + ",")) ||
            (hasEmail && m.AssignedMembers != null &&
             ("," + m.AssignedMembers.ToLower().Replace(", ", ",").Replace(" ,", ",") + ",").Contains("," + email + ",")));
    }

    public static IQueryable<WorkTask> Tasks(AppDbContext db, UserRoleEntry? user, int? filterAdminId = null)
    {
        var projects = Projects(db, user, filterAdminId);
        var query = db.WorkTasks.Where(t => projects.Any(p => p.Id == t.ProjectId));
        if (user == null) return query.Where(_ => false);
        if (HasFullAccess(user)) return query;

        var name = (user.Name ?? "").Trim().ToLower();
        var email = (user.Email ?? "").Trim().ToLower();
        var hasName = name.Length > 0;
        var hasEmail = email.Length > 0;
        return query.Where(t => t.AssignedMember != null &&
            ((hasName && t.AssignedMember.Trim().ToLower() == name) ||
             (hasEmail && t.AssignedMember.Trim().ToLower() == email)));
    }

    public static IQueryable<WorkInvoice> Invoices(AppDbContext db, UserRoleEntry? user, int? filterAdminId = null)
    {
        var projects = Projects(db, user, filterAdminId);
        return db.WorkInvoices.Where(i => projects.Any(p => p.Id == i.ProjectId));
    }

    public static IQueryable<WorkRecurringInvoice> RecurringInvoices(AppDbContext db, UserRoleEntry? user, int? filterAdminId = null)
    {
        var projects = Projects(db, user, filterAdminId);
        return db.WorkRecurringInvoices.Where(i => projects.Any(p => p.Id == i.ProjectId));
    }

    public static IQueryable<WorkPayment> Payments(AppDbContext db, UserRoleEntry? user, int? filterAdminId = null)
    {
        var invoices = Invoices(db, user, filterAdminId);
        return db.WorkPayments.Where(p => invoices.Any(i => i.Id == p.InvoiceId));
    }

    public static bool IsSelf(string? value, UserRoleEntry? user) =>
        user != null &&
        (string.Equals(value?.Trim(), user.Name?.Trim(), StringComparison.OrdinalIgnoreCase) ||
         string.Equals(value?.Trim(), user.Email?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsAssignedToSelf(string? values, UserRoleEntry? user) =>
        (values ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => IsSelf(value, user));

    public static void RestrictProjectContents(WorkProject project, UserRoleEntry? user)
    {
        if (HasFullAccess(user)) return;

        project.ProjectManager = IsSelf(project.ProjectManager, user) ? project.ProjectManager : "";
        project.AssignedMembers = IsAssignedToSelf(project.AssignedMembers, user) ? user?.Name : "";
        project.Milestones = project.Milestones.ToList();
        project.Tasks = project.Tasks.Where(t => IsSelf(t.AssignedMember, user)).ToList();
        project.Files = project.Files.Where(f => IsSelf(f.UploadedBy, user)).ToList();
        project.Timesheets = project.Timesheets.Where(t => IsSelf(t.MemberName, user)).ToList();
        project.Expenses = project.Expenses.Where(e => IsSelf(e.PaidBy, user)).ToList();
        project.WorkNotes = project.WorkNotes.Where(n => IsSelf(n.CreatedBy, user)).ToList();

        foreach (var milestone in project.Milestones)
        {
            milestone.AssignedMembers = IsAssignedToSelf(milestone.AssignedMembers, user) ? user?.Name : "";
            milestone.Tasks = milestone.Tasks.Where(t => IsSelf(t.AssignedMember, user)).ToList();
        }
    }
}
