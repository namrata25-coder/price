using CrmLeadManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Services;

public class SubscriptionUsageSummary
{
    public int AdminId { get; set; }
    public string CompanyName { get; set; } = "";
    public string AdminCode { get; set; } = "";
    public string PlanName { get; set; } = "Free Trial";
    public decimal PlanPrice { get; set; }
    public string BillingInterval { get; set; } = "Monthly";
    public string Status { get; set; } = "Trial";
    public string PaymentStatus { get; set; } = "Pending";
    public DateTime? PurchaseDate { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int DaysRemaining { get; set; }
    public bool IsExpired { get; set; }

    public int UsersUsed { get; set; }
    public int MaxUsers { get; set; }
    public int LeadsUsed { get; set; }
    public int MaxLeads { get; set; }
    public int ProjectsUsed { get; set; }

    public bool HasReachedUserLimit => UsersUsed >= MaxUsers;
    // Plans are limited by users only. Leads are unlimited (MaxLeads <= 0 means unlimited).
    public bool LeadsUnlimited => MaxLeads <= 0;
    public bool HasReachedLeadLimit => false;

    public List<SubscriptionPaymentRecord> RecentPayments { get; set; } = new();
    public BankTransferRequest? PendingBankRequest { get; set; }
}

public interface ISubscriptionLimitService
{
    Task<(bool Allowed, string Reason, int CurrentCount, int MaxAllowed)> CanAddLeadAsync(int adminId);
    Task<(bool Allowed, string Reason, int CurrentCount, int MaxAllowed)> CanAddUserAsync(int adminId);
    Task<SubscriptionUsageSummary> GetUsageAsync(int adminId);
}

public class SubscriptionLimitService : ISubscriptionLimitService
{
    private readonly AppDbContext _db;

    public SubscriptionLimitService(AppDbContext db)
    {
        _db = db;
    }

    private async Task<CompanySubscription> EnsureSubscriptionAsync(int adminId)
    {
        var sub = await _db.CompanySubscriptions.FirstOrDefaultAsync(s => s.AdminId == adminId);
        if (sub == null)
        {
            var defaultPlan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == "trial" || p.Price == 0)
                           ?? await _db.SubscriptionPlans.OrderBy(p => p.DisplayOrder).FirstOrDefaultAsync();

            sub = new CompanySubscription
            {
                AdminId = adminId,
                PlanId = defaultPlan?.Id ?? 1,
                PlanName = defaultPlan?.Name ?? "Free Trial",
                PlanPrice = defaultPlan?.Price ?? 0m,
                Status = "Trial",
                PaymentStatus = "Pending",
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.AddDays(defaultPlan?.TrialDays > 0 ? defaultPlan.TrialDays : 7),
                MaxUsers = defaultPlan?.MaxTeamMembers ?? 3,
                MaxLeads = defaultPlan?.MaxLeads ?? 10,
                AmountPaid = 0m,
                PaymentMethod = "Free Trial",
                Notes = "Auto-created trial subscription"
            };
            _db.CompanySubscriptions.Add(sub);
            await _db.SaveChangesAsync();
        }
        else
        {
            // Auto-check expiry
            if (DateTime.UtcNow > sub.EndDate && sub.Status != "Expired")
            {
                sub.Status = "Expired";
                await _db.SaveChangesAsync();
            }

            int expectedUsers = 3;
            if (sub.PlanName.Contains("Pro", StringComparison.OrdinalIgnoreCase)) expectedUsers = 15;
            else if (sub.PlanName.Contains("Starter", StringComparison.OrdinalIgnoreCase)) expectedUsers = 5;
            else if (sub.PlanName.Contains("Enterprise Plus", StringComparison.OrdinalIgnoreCase)) expectedUsers = 250;
            else if (sub.PlanName.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)) expectedUsers = 100;

            if (expectedUsers > 3 && sub.MaxUsers != expectedUsers)
            {
                sub.MaxUsers = expectedUsers;
                await _db.SaveChangesAsync();
            }
        }
        return sub;
    }

    public async Task<(bool Allowed, string Reason, int CurrentCount, int MaxAllowed)> CanAddLeadAsync(int adminId)
    {
        if (adminId <= 0) return (true, "", 0, 999);

        var sub = await EnsureSubscriptionAsync(adminId);
        int currentCount = await _db.Leads.CountAsync(l => l.AdminId == adminId);
        // Leads are unlimited on every plan - subscription is based on number of users only.
        const int maxAllowed = 0; // 0 = unlimited

        if (sub.IsExpired)
        {
            return (false, "Your subscription plan has expired. Please renew or upgrade your plan to add new leads.", currentCount, maxAllowed);
        }

        if (string.Equals(sub.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sub.Status, "Suspended", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"Your subscription is currently {sub.Status}. Please reactivate your plan to add leads.", currentCount, maxAllowed);
        }

        return (true, "", currentCount, maxAllowed);
    }

    public async Task<(bool Allowed, string Reason, int CurrentCount, int MaxAllowed)> CanAddUserAsync(int adminId)
    {
        if (adminId <= 0) return (true, "", 0, 999);

        var sub = await EnsureSubscriptionAsync(adminId);
        // Total users = 1 company admin + team members
        int currentCount = await _db.Users.CountAsync(u => u.AdminId == adminId || u.Id == adminId);
        int maxAllowed = 3;
        if (sub.PlanName.Contains("Pro", StringComparison.OrdinalIgnoreCase)) maxAllowed = 15;
        else if (sub.PlanName.Contains("Starter", StringComparison.OrdinalIgnoreCase)) maxAllowed = 5;
        else if (sub.PlanName.Contains("Enterprise Plus", StringComparison.OrdinalIgnoreCase)) maxAllowed = 250;
        else if (sub.PlanName.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)) maxAllowed = 100;
        else if (sub.MaxUsers > 0) maxAllowed = sub.MaxUsers;

        if (sub.IsExpired)
        {
            return (false, "Your subscription plan has expired. Please renew or upgrade your plan to add new team members.", currentCount, maxAllowed);
        }

        if (string.Equals(sub.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sub.Status, "Suspended", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"Your subscription is currently {sub.Status}. Please reactivate your plan to add team members.", currentCount, maxAllowed);
        }

        if (currentCount >= maxAllowed)
        {
            return (false, $"User limit reached ({currentCount} / {maxAllowed} Users). Please upgrade your subscription plan to add more team members.", currentCount, maxAllowed);
        }

        return (true, "", currentCount, maxAllowed);
    }

    public async Task<SubscriptionUsageSummary> GetUsageAsync(int adminId)
    {
        var sub = await EnsureSubscriptionAsync(adminId);
        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Id == adminId);
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == sub.PlanId)
                ?? await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Name == sub.PlanName);

        int usersUsed = await _db.Users.CountAsync(u => u.AdminId == adminId || u.Id == adminId);
        int leadsUsed = await _db.Leads.CountAsync(l => l.AdminId == adminId);
        int projectsUsed = await _db.WorkProjects.CountAsync(p => p.AdminId == adminId);

        var recentPayments = await _db.SubscriptionPayments
            .Where(p => p.AdminId == adminId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(10)
            .ToListAsync();

        var pendingBank = await _db.BankTransferRequests
            .Where(r => r.AdminId == adminId && r.Status == "Pending")
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        int resolvedMaxUsers = 3;
        if (sub.PlanName.Contains("Pro", StringComparison.OrdinalIgnoreCase)) resolvedMaxUsers = 15;
        else if (sub.PlanName.Contains("Starter", StringComparison.OrdinalIgnoreCase)) resolvedMaxUsers = 5;
        else if (sub.PlanName.Contains("Enterprise Plus", StringComparison.OrdinalIgnoreCase)) resolvedMaxUsers = 250;
        else if (sub.PlanName.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)) resolvedMaxUsers = 100;
        else if (sub.MaxUsers > 0) resolvedMaxUsers = sub.MaxUsers;
        else if (plan != null && plan.MaxTeamMembers > 0) resolvedMaxUsers = plan.MaxTeamMembers;

        return new SubscriptionUsageSummary
        {
            AdminId = adminId,
            CompanyName = admin?.CompanyName ?? "Company",
            AdminCode = admin?.AdminCode ?? $"ADM-{adminId:D4}",
            PlanName = sub.PlanName,
            PlanPrice = sub.PlanPrice > 0 ? sub.PlanPrice : (plan?.Price ?? 0m),
            BillingInterval = plan?.BillingInterval ?? "Monthly",
            Status = sub.Status,
            PaymentStatus = sub.PaymentStatus,
            PurchaseDate = sub.PurchaseDate,
            StartDate = sub.StartDate,
            EndDate = sub.EndDate,
            DaysRemaining = sub.DaysRemaining,
            IsExpired = sub.IsExpired,
            UsersUsed = usersUsed,
            MaxUsers = resolvedMaxUsers,
            LeadsUsed = leadsUsed,
            MaxLeads = 0, // unlimited - plans are limited by users only
            ProjectsUsed = projectsUsed,
            RecentPayments = recentPayments,
            PendingBankRequest = pendingBank
        };
    }
}

