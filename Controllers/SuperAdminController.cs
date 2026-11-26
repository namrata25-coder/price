using System.Security.Claims;
using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using CrmLeadManagement.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers;

#region View Models & DTOs

public class CompanyItemViewModel
{
    public int Id { get; set; }
    public string AdminCode { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string Status { get; set; } = "Active";
    public string PlanName { get; set; } = "Free Trial";
    public string PlanStatus { get; set; } = "Trial";
    public int MaxUsers { get; set; } = 3;
    public int MaxLeads { get; set; } = 100;
    public int LeadsCount { get; set; }
    public int ProjectsCount { get; set; }
    public int MembersCount { get; set; }
    public int DaysRemaining { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class SuperAdminDashboardVm
{
    public int TotalCompanies { get; set; }
    public int ActiveCompanies { get; set; }
    public int FreeTrialsCount { get; set; }
    public int TotalPlatformLeads { get; set; }
    public decimal EstimatedMRR { get; set; }
    public List<CompanyItemViewModel> RecentCompanies { get; set; } = new();
    public List<SaaSSubscriptionPlan> Plans { get; set; } = new();
}

public class SuperAdminSubscriptionsVm
{
    public List<CompanySubscriptionItemVm> Subscriptions { get; set; } = new();
    public List<SaaSSubscriptionPlan> AvailablePlans { get; set; } = new();
    public int TotalCount { get; set; }
    public int TrialCount { get; set; }
    public int ActiveCount { get; set; }
    public int ExpiredCount { get; set; }
}

public class CompanySubscriptionItemVm
{
    public int SubscriptionId { get; set; }
    public int AdminId { get; set; }
    public string CompanyName { get; set; } = "";
    public string AdminCode { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string? ContactPhone { get; set; }
    public string PlanName { get; set; } = "Free Trial";
    public int PlanId { get; set; }
    public string Status { get; set; } = "Trial";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int MaxLeads { get; set; }
    public int LeadsUsed { get; set; }
    public int MaxUsers { get; set; }
    public int UsersCount { get; set; }
    public int ProjectsUsed { get; set; }
    public decimal PlanPrice { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public string PaymentStatus { get; set; } = "Pending";
    public int DaysRemaining { get; set; }
    public bool IsExpired { get; set; }
    public decimal AmountPaid { get; set; }
    public bool HasPendingBankRequest { get; set; }
    public int? PendingBankRequestId { get; set; }
    public string? PendingBankPlan { get; set; }
    public decimal PendingBankAmount { get; set; }
}

public class CreateCompanyDto
{
    // The 4 main details requested by the user:
    public string CompanyName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }

    // Additional configuration:
    public string? Password { get; set; }
    public string? PlanCode { get; set; } = "trial";
    public string? Status { get; set; } = "Active";
    public string? ReturnUrl { get; set; }
}

public class UpdateCompanyDto
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string Status { get; set; } = "Active";
    public string? NewPassword { get; set; }
}

public class UpdateCompanySubscriptionDto
{
    public int AdminId { get; set; }
    public int PlanId { get; set; }
    public string Status { get; set; } = "Active";
    public int ExtendDays { get; set; } = 30;
    public int? CustomMaxUsers { get; set; }
}

#endregion

/// <summary>
/// Dedicated SaaS Platform Controller for SuperAdmin.
/// Provides:
/// 1. Dashboard
/// 2. User (Companies management + Add User with 4 company details)
/// 3. Subscription (Lead limits + "Free Trial" / "Open Admin Panel" click action)
/// 4. CMS (Landing Page, FAQ Articles, Pricing & Plans)
/// 5. Email Templates
/// 6. Setting
/// </summary>
[Authorize]
public class SuperAdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<SuperAdminController> _logger;
    private readonly ITokenService _tokenService;
    private readonly ISaaSEmailService _emailService;
    private readonly ISaaSPaymentService _paymentService;
    private readonly PasswordHasher<UserRoleEntry> _passwordHasher;

    public SuperAdminController(
        AppDbContext db, 
        ILogger<SuperAdminController> logger, 
        ITokenService tokenService,
        ISaaSEmailService emailService,
        ISaaSPaymentService paymentService)
    {
        _db = db;
        _logger = logger;
        _tokenService = tokenService;
        _emailService = emailService;
        _paymentService = paymentService;
        _passwordHasher = new PasswordHasher<UserRoleEntry>();
    }

    private UserRoleEntry GetCurrentSuperAdmin()
    {
        var currentUser = PermissionChecker.GetCurrentUser(_db, User);
        if (currentUser != null)
        {
            return currentUser;
        }
        return _db.Users.FirstOrDefault(u => u.Role == "Super Admin")
            ?? new UserRoleEntry { Name = "Super Administrator", Email = "superadmin@visioncrm.com", Role = "Super Admin" };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. DASHBOARD
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    [Route("SuperAdmin")]
    [Route("SuperAdmin/Dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var superAdmin = GetCurrentSuperAdmin();

        ViewData["Title"] = "SuperAdmin Dashboard · SaaS Platform";
        ViewData["ActivePage"] = "SuperAdminDashboard";

        var adminEntities = await _db.Users
            .Where(u => u.Role == "Admin")
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        var subscriptions = await _db.CompanySubscriptions.ToListAsync();
        var plans = await _db.SubscriptionPlans.OrderBy(p => p.DisplayOrder).ToListAsync();

        var totalLeads = await _db.Leads.CountAsync();
        var totalCompanies = adminEntities.Count;
        var activeCompanies = adminEntities.Count(u => string.Equals(u.Status, "Active", StringComparison.OrdinalIgnoreCase));
        
        var freeTrialsCount = subscriptions.Count(s => string.Equals(s.Status, "Trial", StringComparison.OrdinalIgnoreCase) && !s.IsExpired);

        // Estimate MRR: sum prices of paid active subscriptions
        decimal mrr = 0m;
        foreach (var sub in subscriptions.Where(s => string.Equals(s.Status, "Active", StringComparison.OrdinalIgnoreCase)))
        {
            var p = plans.FirstOrDefault(x => x.Id == sub.PlanId);
            if (p != null) mrr += p.Price;
        }

        var recentList = new List<CompanyItemViewModel>();
        foreach (var admin in adminEntities.Take(8))
        {
            var sub = subscriptions.FirstOrDefault(s => s.AdminId == admin.Id);
            var leadsCount = await _db.Leads.CountAsync(l => l.AdminId == admin.Id);

            recentList.Add(new CompanyItemViewModel
            {
                Id = admin.Id,
                AdminCode = !string.IsNullOrWhiteSpace(admin.AdminCode) ? admin.AdminCode : $"ADM-{admin.Id:D4}",
                CompanyName = !string.IsNullOrWhiteSpace(admin.CompanyName) ? admin.CompanyName : "Organization",
                Name = admin.Name ?? "Admin",
                Email = admin.Email ?? "",
                Phone = admin.Phone,
                Status = admin.Status ?? "Active",
                PlanName = sub?.PlanName ?? "Free Trial",
                PlanStatus = sub?.Status ?? "Trial",
                MaxLeads = sub?.MaxLeads ?? 100,
                LeadsCount = leadsCount,
                DaysRemaining = sub?.DaysRemaining ?? 0,
                CreatedAt = admin.CreatedAt,
                LastLoginAt = admin.LastLoginAt
            });
        }

        var vm = new SuperAdminDashboardVm
        {
            TotalCompanies = totalCompanies,
            ActiveCompanies = activeCompanies,
            FreeTrialsCount = freeTrialsCount,
            TotalPlatformLeads = totalLeads,
            EstimatedMRR = mrr,
            RecentCompanies = recentList,
            Plans = plans
        };

        return View(vm);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. USER (COMPANIES MANAGEMENT + ADD USER WITH 4 DETAILS)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    [Route("SuperAdmin/Users")]
    [Route("SuperAdmin/Admins")] // Backwards-compatible route
    public async Task<IActionResult> Users(string? search = null, string? status = null, string? plan = null)
    {
        var superAdmin = GetCurrentSuperAdmin();

        ViewData["Title"] = "User / Companies · SuperAdmin Panel";
        ViewData["ActivePage"] = "SuperAdminUsers";

        var adminsQuery = _db.Users.Where(u => u.Role == "Admin").AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            adminsQuery = adminsQuery.Where(u =>
                (u.Name != null && u.Name.ToLower().Contains(s)) ||
                (u.Email != null && u.Email.ToLower().Contains(s)) ||
                (u.CompanyName != null && u.CompanyName.ToLower().Contains(s)) ||
                (u.Phone != null && u.Phone.ToLower().Contains(s)) ||
                (u.AdminCode != null && u.AdminCode.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            adminsQuery = adminsQuery.Where(u => u.Status == status);
        }

        var adminEntities = await adminsQuery.OrderByDescending(u => u.Id).ToListAsync();
        var subscriptions = await _db.CompanySubscriptions.ToListAsync();
        var availablePlans = await _db.SubscriptionPlans.Where(p => p.IsActive).OrderBy(p => p.DisplayOrder).ToListAsync();

        var companyVms = new List<CompanyItemViewModel>();
        foreach (var admin in adminEntities)
        {
            var sub = subscriptions.FirstOrDefault(s => s.AdminId == admin.Id);
            if (!string.IsNullOrWhiteSpace(plan) && sub != null && !string.Equals(sub.PlanName, plan, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var leadsCount = await _db.Leads.CountAsync(l => l.AdminId == admin.Id);
            var projectsCount = await _db.WorkProjects.CountAsync(p => p.AdminId == admin.Id);
            var membersCount = await _db.Users.CountAsync(u => u.AdminId == admin.Id && u.Id != admin.Id);

            var planEntity = availablePlans.FirstOrDefault(p => p.Id == sub?.PlanId)
                ?? availablePlans.FirstOrDefault(p => string.Equals(p.Name, sub?.PlanName, StringComparison.OrdinalIgnoreCase))
                ?? availablePlans.FirstOrDefault(p => sub != null && sub.PlanName.ToLower().Contains("starter") && p.Name.ToLower().Contains("starter"))
                ?? availablePlans.FirstOrDefault(p => sub != null && sub.PlanName.ToLower().Contains("pro") && p.Name.ToLower().Contains("pro"));

            int planUsers = 3;
            if (sub != null && sub.PlanName.Contains("Pro", StringComparison.OrdinalIgnoreCase)) planUsers = 15;
            else if (sub != null && sub.PlanName.Contains("Starter", StringComparison.OrdinalIgnoreCase)) planUsers = 5;
            else if (planEntity != null && planEntity.MaxTeamMembers > 0) planUsers = planEntity.MaxTeamMembers;
            else if (sub?.MaxUsers > 0) planUsers = sub.MaxUsers;

            var maxUsers = planUsers;
            var maxLeads = planEntity?.MaxLeads > 0 ? planEntity.MaxLeads : (sub?.MaxLeads > 0 ? sub.MaxLeads : 100);

            if (sub != null && sub.MaxUsers != maxUsers)
            {
                sub.MaxUsers = maxUsers;
            }

            companyVms.Add(new CompanyItemViewModel
            {
                Id = admin.Id,
                AdminCode = !string.IsNullOrWhiteSpace(admin.AdminCode) ? admin.AdminCode : $"ADM-{admin.Id:D4}",
                CompanyName = !string.IsNullOrWhiteSpace(admin.CompanyName) ? admin.CompanyName : "Company",
                Name = admin.Name ?? "Admin",
                Email = admin.Email ?? "",
                Phone = admin.Phone,
                Status = admin.Status ?? "Active",
                PlanName = sub?.PlanName ?? "Free Trial",
                PlanStatus = sub?.Status ?? "Trial",
                MaxUsers = maxUsers,
                MaxLeads = maxLeads,
                LeadsCount = leadsCount,
                ProjectsCount = projectsCount,
                MembersCount = membersCount,
                DaysRemaining = sub?.DaysRemaining ?? 0,
                CreatedAt = admin.CreatedAt,
                LastLoginAt = admin.LastLoginAt
            });
        }

        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.Plan = plan;
        ViewBag.AvailablePlans = availablePlans;

        return View(companyVms);
    }

    // POST: /SuperAdmin/CreateCompany (User - Add User with 4 Details)
    [HttpPost]
    [Route("SuperAdmin/CreateCompany")]
    [Route("SuperAdmin/CreateAdmin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCompany(CreateCompanyDto dto)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null)
        {
            TempData["ErrorMessage"] = "Access Denied.";
            return RedirectToAction(nameof(Users));
        }

        // Validate the 4 essential details requested by user:
        if (string.IsNullOrWhiteSpace(dto.CompanyName))
        {
            TempData["ErrorMessage"] = "Company Name is required.";
            return RedirectToAction(nameof(Users));
        }
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            TempData["ErrorMessage"] = "Company Manager / Admin Name is required.";
            return RedirectToAction(nameof(Users));
        }
        if (string.IsNullOrWhiteSpace(dto.Email))
        {
            TempData["ErrorMessage"] = "Email Address is required.";
            return RedirectToAction(nameof(Users));
        }

        var emailLower = dto.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == emailLower))
        {
            TempData["ErrorMessage"] = $"A user or company with email '{dto.Email}' already exists.";
            return RedirectToAction(nameof(Users));
        }

        // Generate sequential Admin Code (e.g. ADM-1002)
        var existingCodes = await _db.Users
            .Where(u => u.Role == "Admin" && !string.IsNullOrWhiteSpace(u.AdminCode))
            .Select(u => u.AdminCode!)
            .ToListAsync();

        var maxNum = existingCodes
            .Where(c => c.StartsWith("ADM-", StringComparison.OrdinalIgnoreCase))
            .Select(c => int.TryParse(c.Substring(4), out var v) ? v : 1000)
            .DefaultIfEmpty(1000)
            .Max();

        var nextAdminCode = $"ADM-{Math.Max(maxNum + 1, 1001)}";

        // Password logic: use provided password or generate clean initial password
        var password = !string.IsNullOrWhiteSpace(dto.Password) 
            ? dto.Password.Trim() 
            : $"{dto.Name.Trim().Split(' ')[0]}@123!";

        var newAdmin = new UserRoleEntry
        {
            Name = dto.Name.Trim(),
            Email = emailLower,
            Role = "Admin",
            CompanyName = dto.CompanyName.Trim(),
            Phone = dto.Phone?.Trim(),
            Status = string.IsNullOrWhiteSpace(dto.Status) ? "Active" : dto.Status.Trim(),
            AdminCode = nextAdminCode,
            CreatedAt = DateTime.UtcNow
        };
        newAdmin.PasswordHash = _passwordHasher.HashPassword(newAdmin, password);

        _db.Users.Add(newAdmin);
        await _db.SaveChangesAsync();

        newAdmin.AdminId = newAdmin.Id;
        await _db.SaveChangesAsync();

        // Assign Subscription Plan (defaults to Free Trial or chosen plan in Trial status)
        var selectedPlan = (!string.IsNullOrWhiteSpace(dto.PlanCode) && dto.PlanCode != "trial"
                                ? await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == dto.PlanCode || p.Name == dto.PlanCode)
                                : null)
                        ?? await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == "trial" || p.Price == 0)
                        ?? new SaaSSubscriptionPlan { Id = 1, Name = "Free Trial", MaxLeads = 3, TrialDays = 14 };

        var trialDays = selectedPlan.TrialDays > 0 ? selectedPlan.TrialDays : 14;
        var sub = new CompanySubscription
        {
            AdminId = newAdmin.Id,
            PlanId = selectedPlan.Id,
            PlanName = selectedPlan.Name,
            Status = "Trial",
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(trialDays),
            MaxLeads = selectedPlan.MaxLeads,
            AmountPaid = 0.00m,
            PaymentMethod = "Free Trial",
            Notes = $"Onboarded by SuperAdmin ({superAdmin.Email})"
        };
        _db.CompanySubscriptions.Add(sub);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Company {CompanyName} created with Admin ID {AdminCode} and plan {PlanName}.",
            newAdmin.CompanyName, newAdmin.AdminCode, sub.PlanName);

        // Dispatch Welcome & Free Trial email to the newly onboarded company administrator
        _ = Task.Run(async () =>
        {
            try
            {
                var loginUrl = $"{Request.Scheme}://{Request.Host}/login";
                await _emailService.SendTemplatedEmailAsync(
                    "WELCOME_TRIAL",
                    newAdmin.Email,
                    newAdmin.Name ?? "Admin",
                    new Dictionary<string, string>
                    {
                        { "AdminName", newAdmin.Name ?? "Admin" },
                        { "CompanyName", newAdmin.CompanyName ?? "Your Company" },
                        { "AdminCode", newAdmin.AdminCode ?? "" },
                        { "LoginUrl", loginUrl },
                        { "PlanName", sub.PlanName },
                        { "LeadLimit", sub.MaxLeads.ToString("N0") },
                        { "MaxUsers", sub.MaxUsers.ToString("N0") },
                        { "ExpiryDate", sub.EndDate.ToString("dd MMM yyyy") }
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send WELCOME_TRIAL email to {Email}", newAdmin.Email);
            }
        });

        TempData["SuccessMessage"] = $"Company '{newAdmin.CompanyName}' added successfully! Assigned Admin ID: {newAdmin.AdminCode}. Password: '{password}'";
        if (string.Equals(dto.ReturnUrl, "Dashboard", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(Dashboard));
        }
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [Route("SuperAdmin/UpdateCompany")]
    [Route("SuperAdmin/UpdateAdmin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCompany(UpdateCompanyDto dto)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Users));

        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Id == dto.Id && u.Role == "Admin");
        if (admin == null)
        {
            TempData["ErrorMessage"] = "Company account not found.";
            return RedirectToAction(nameof(Users));
        }

        admin.CompanyName = !string.IsNullOrWhiteSpace(dto.CompanyName) ? dto.CompanyName.Trim() : admin.CompanyName;
        admin.Name = !string.IsNullOrWhiteSpace(dto.Name) ? dto.Name.Trim() : admin.Name;
        admin.Phone = dto.Phone?.Trim();
        admin.Status = !string.IsNullOrWhiteSpace(dto.Status) ? dto.Status.Trim() : admin.Status;

        if (!string.IsNullOrWhiteSpace(dto.NewPassword))
        {
            admin.PasswordHash = _passwordHasher.HashPassword(admin, dto.NewPassword.Trim());
            admin.LastPasswordChangeAt = DateTime.UtcNow;
            TempData["SuccessMessage"] = $"Company '{admin.CompanyName}' updated and password was reset.";

            // Send Password Reset notification email
            _ = Task.Run(async () =>
            {
                try
                {
                    var loginUrl = $"{Request.Scheme}://{Request.Host}/login";
                    await _emailService.SendTemplatedEmailAsync(
                        "PASSWORD_RESET",
                        admin.Email,
                        admin.Name ?? "Admin",
                        new Dictionary<string, string>
                        {
                            { "AdminName", admin.Name ?? "Admin" },
                            { "CompanyName", admin.CompanyName ?? "Your Company" },
                            { "LoginUrl", loginUrl }
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send PASSWORD_RESET email to {Email}", admin.Email);
                }
            });
        }
        else
        {
            TempData["SuccessMessage"] = $"Company '{admin.CompanyName}' details updated successfully.";
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [Route("SuperAdmin/ToggleCompanyStatus")]
    [Route("SuperAdmin/ToggleCompanyStatus/{id:int}")]
    [Route("SuperAdmin/ToggleStatus")]
    [Route("SuperAdmin/ToggleStatus/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCompanyStatus(int id)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Users));

        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == "Admin");
        if (admin == null)
        {
            TempData["ErrorMessage"] = "Company not found.";
            return RedirectToAction(nameof(Users));
        }

        admin.Status = string.Equals(admin.Status, "Active", StringComparison.OrdinalIgnoreCase) ? "Inactive" : "Active";
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Company '{admin.CompanyName}' status updated to {admin.Status}.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [Route("SuperAdmin/DeleteCompany")]
    [Route("SuperAdmin/DeleteCompany/{id:int}")]
    [Route("SuperAdmin/DeleteAdmin")]
    [Route("SuperAdmin/DeleteAdmin/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCompany(int id)
    {
        var superAdmin = GetCurrentSuperAdmin();

        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == "Admin");
        if (admin == null)
        {
            TempData["ErrorMessage"] = "Company not found.";
            return RedirectToAction(nameof(Users));
        }

        if (id == 1 || string.Equals(admin.AdminCode, "ADM-1001", StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "The default primary administrator cannot be deleted. You can deactivate it instead.";
            return RedirectToAction(nameof(Users));
        }

        try
        {
            // 1. Clean up AccessTokens for this company admin and staff
            var teamMembers = await _db.Users.Where(u => u.AdminId == id).ToListAsync();
            var allUserIds = teamMembers.Select(u => u.Id).Append(id).Distinct().ToList();

            var tokens = await _db.AccessTokens.Where(t => allUserIds.Contains(t.UserId)).ToListAsync();
            if (tokens.Any()) _db.AccessTokens.RemoveRange(tokens);

            // 2. Clean up CompanySubscriptions
            var subs = await _db.CompanySubscriptions.Where(s => s.AdminId == id).ToListAsync();
            if (subs.Any()) _db.CompanySubscriptions.RemoveRange(subs);

            // 3. Clean up staff members
            var staff = teamMembers.Where(u => u.Id != id).ToList();
            if (staff.Any()) _db.Users.RemoveRange(staff);

            // 4. Clean up leads for this tenant
            var leads = await _db.Leads.Where(l => l.AdminId == id).ToListAsync();
            if (leads.Any()) _db.Leads.RemoveRange(leads);

            // 5. Remove the company admin
            _db.Users.Remove(admin);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Company '{admin.CompanyName}' ({admin.AdminCode}) was deleted successfully.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete company {Id}", id);
            admin.Status = "Inactive";
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Company '{admin.CompanyName}' was marked as Inactive.";
        }

        return RedirectToAction(nameof(Users));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. SUBSCRIPTION (FOR LEADS, FREE TRIAL CLICK -> OPEN ADMIN PANEL)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    [Route("SuperAdmin/Subscriptions")]
    public async Task<IActionResult> Subscriptions(string? filter = null, string? search = null)
    {
        var superAdmin = GetCurrentSuperAdmin();

        ViewData["Title"] = "Subscriptions & Lead Quotas · SuperAdmin Panel";
        ViewData["ActivePage"] = "SuperAdminSubscriptions";

        var admins = await _db.Users.Where(u => u.Role == "Admin").ToListAsync();
        var subs = await _db.CompanySubscriptions.ToListAsync();
        var plans = await _db.SubscriptionPlans.OrderBy(p => p.DisplayOrder).ToListAsync();
        var pendingBankReqs = await _db.BankTransferRequests.Where(r => r.Status == "Pending").ToListAsync();

        var subItems = new List<CompanySubscriptionItemVm>();
        foreach (var admin in admins)
        {
            var sub = subs.FirstOrDefault(s => s.AdminId == admin.Id);
            var pendingBank = pendingBankReqs.FirstOrDefault(r => r.AdminId == admin.Id);
            if (sub == null)
            {
                // Auto-create default trial subscription if missing
                var defaultPlan = plans.FirstOrDefault(p => p.Code == "trial") ?? plans.FirstOrDefault() 
                               ?? new SaaSSubscriptionPlan { Id = 1, Name = "Free Trial", MaxLeads = 3, TrialDays = 7 };
                sub = new CompanySubscription
                {
                    AdminId = admin.Id,
                    PlanId = defaultPlan.Id,
                    PlanName = defaultPlan.Name,
                    Status = "Trial",
                    StartDate = DateTime.UtcNow,
                    EndDate = DateTime.UtcNow.AddDays(14),
                    MaxLeads = defaultPlan.MaxLeads,
                    PaymentMethod = "Free Trial"
                };
                _db.CompanySubscriptions.Add(sub);
                await _db.SaveChangesAsync();
            }

            var leadsUsed = await _db.Leads.CountAsync(l => l.AdminId == admin.Id);
            var projectsUsed = await _db.WorkProjects.CountAsync(p => p.AdminId == admin.Id);
            var memberCount = await _db.Users.CountAsync(u => u.AdminId == admin.Id && u.Id != admin.Id);
            var currentPlan = plans.FirstOrDefault(p => p.Id == sub.PlanId)
                ?? plans.FirstOrDefault(p => string.Equals(p.Name, sub.PlanName, StringComparison.OrdinalIgnoreCase));
            var maxUsers = sub.MaxUsers > 0 ? sub.MaxUsers : (currentPlan?.MaxTeamMembers ?? 3);
            var maxLeads = sub.MaxLeads > 0 ? sub.MaxLeads : (currentPlan?.MaxLeads ?? 100);

            subItems.Add(new CompanySubscriptionItemVm
            {
                SubscriptionId = sub.Id,
                AdminId = admin.Id,
                CompanyName = admin.CompanyName ?? "Company",
                AdminCode = admin.AdminCode ?? $"ADM-{admin.Id:D4}",
                ContactName = admin.Name ?? "Admin",
                ContactEmail = admin.Email ?? "",
                ContactPhone = admin.Phone,
                PlanName = sub.PlanName,
                PlanId = sub.PlanId,
                Status = sub.Status,
                StartDate = sub.StartDate,
                EndDate = sub.EndDate,
                MaxLeads = maxLeads,
                LeadsUsed = leadsUsed,
                MaxUsers = maxUsers,
                UsersCount = memberCount + 1,
                ProjectsUsed = projectsUsed,
                PlanPrice = sub.PlanPrice > 0 ? sub.PlanPrice : (currentPlan?.Price ?? 0m),
                PurchaseDate = sub.PurchaseDate,
                PaymentStatus = sub.PaymentStatus,
                DaysRemaining = sub.DaysRemaining,
                IsExpired = sub.IsExpired,
                AmountPaid = sub.AmountPaid,
                HasPendingBankRequest = pendingBank != null,
                PendingBankRequestId = pendingBank?.Id,
                PendingBankPlan = pendingBank?.PlanName,
                PendingBankAmount = pendingBank?.Amount ?? 0m
            });
        }

        // Filtering
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            subItems = subItems.Where(x =>
                x.CompanyName.ToLower().Contains(s) ||
                x.AdminCode.ToLower().Contains(s) ||
                x.ContactEmail.ToLower().Contains(s) ||
                x.PlanName.ToLower().Contains(s)).ToList();
        }

        if (string.Equals(filter, "Trial", StringComparison.OrdinalIgnoreCase))
        {
            subItems = subItems.Where(x => string.Equals(x.Status, "Trial", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else if (string.Equals(filter, "Active", StringComparison.OrdinalIgnoreCase))
        {
            subItems = subItems.Where(x => string.Equals(x.Status, "Active", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else if (string.Equals(filter, "Expired", StringComparison.OrdinalIgnoreCase))
        {
            subItems = subItems.Where(x => x.IsExpired || string.Equals(x.Status, "Expired", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var vm = new SuperAdminSubscriptionsVm
        {
            Subscriptions = subItems.OrderBy(s => s.DaysRemaining).ToList(),
            AvailablePlans = plans,
            TotalCount = subs.Count,
            TrialCount = subs.Count(s => string.Equals(s.Status, "Trial", StringComparison.OrdinalIgnoreCase)),
            ActiveCount = subs.Count(s => string.Equals(s.Status, "Active", StringComparison.OrdinalIgnoreCase)),
            ExpiredCount = subs.Count(s => s.IsExpired || string.Equals(s.Status, "Expired", StringComparison.OrdinalIgnoreCase))
        };

        ViewBag.CurrentFilter = filter;
        ViewBag.Search = search;

        return View(vm);
    }

    [HttpPost]
    [Route("SuperAdmin/UpdateSubscription")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSubscription(UpdateCompanySubscriptionDto dto)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Subscriptions));

        var sub = await _db.CompanySubscriptions.FirstOrDefaultAsync(s => s.AdminId == dto.AdminId);
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == dto.PlanId);

        if (sub != null && plan != null)
        {
            sub.PlanId = plan.Id;
            sub.PlanName = plan.Name;
            sub.Status = dto.Status;
            sub.MaxUsers = dto.CustomMaxUsers.HasValue && dto.CustomMaxUsers.Value > 0 ? dto.CustomMaxUsers.Value : plan.MaxTeamMembers;
            sub.MaxLeads = plan.MaxLeads; // leads are not limited per plan
            
            if (dto.ExtendDays > 0)
            {
                var baseDate = sub.EndDate > DateTime.UtcNow ? sub.EndDate : DateTime.UtcNow;
                sub.EndDate = baseDate.AddDays(dto.ExtendDays);
            }

            await _db.SaveChangesAsync();

            // Notify company admin of plan activation/update
            var adminUser = await _db.Users.FindAsync(dto.AdminId);
            if (adminUser != null && !string.IsNullOrWhiteSpace(adminUser.Email))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emailService.SendTemplatedEmailAsync(
                            "PLAN_ACTIVATED",
                            adminUser.Email,
                            adminUser.Name ?? "Admin",
                            new Dictionary<string, string>
                            {
                                { "AdminName", adminUser.Name ?? "Admin" },
                                { "CompanyName", adminUser.CompanyName ?? "Your Company" },
                                { "PlanName", sub.PlanName },
                                { "LeadLimit", sub.MaxLeads.ToString("N0") },
                                { "MaxUsers", sub.MaxUsers.ToString("N0") },
                                { "ExpiryDate", sub.EndDate.ToString("dd MMM yyyy") }
                            });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send PLAN_ACTIVATED email to {Email}", adminUser.Email);
                    }
                });
            }

            TempData["SuccessMessage"] = $"Subscription updated for company! Plan: {sub.PlanName}, Max Users: {sub.MaxUsers:N0}, Leads: Unlimited.";
        }

        return RedirectToAction(nameof(Subscriptions));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OPEN ADMIN PANEL / IMPERSONATION (FREE TRIAL CLICK -> DIRECT ADMIN PANEL)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    [Route("SuperAdmin/OpenAdminPanel/{id:int}")]
    [Route("SuperAdmin/Impersonate/{id:int}")]
    public async Task<IActionResult> OpenAdminPanel(int id)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null)
        {
            TempData["ErrorMessage"] = "Access Denied: Super Admin required.";
            return RedirectToAction(nameof(Dashboard));
        }

        var targetAdmin = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (targetAdmin == null)
        {
            TempData["ErrorMessage"] = "User account not found.";
            return RedirectToAction(nameof(Users));
        }

        // Store impersonation markers in cookies so layout shows the top banner and we can exit anytime
        Response.Cookies.Append("ImpersonatedBySuperAdmin", superAdmin.Email ?? "superadmin@visioncrm.com", new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax });
        Response.Cookies.Append("ImpersonatedCompanyName", targetAdmin.CompanyName ?? targetAdmin.Name ?? "Company", new CookieOptions { HttpOnly = false, SameSite = SameSiteMode.Lax });
        Response.Cookies.Append("ImpersonatedAdminCode", targetAdmin.AdminCode ?? $"ADM-{targetAdmin.Id:D4}", new CookieOptions { HttpOnly = false, SameSite = SameSiteMode.Lax });

        var (plainToken, _) = await _tokenService.IssueTokenAsync(targetAdmin.Id, "impersonation");
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, targetAdmin.Id.ToString()),
            new(ClaimTypes.Name, targetAdmin.Name ?? "Admin"),
            new(ClaimTypes.Email, targetAdmin.Email ?? ""),
            new(ClaimTypes.Role, targetAdmin.Role ?? "Admin"),
            new("token_id", plainToken),
            new("admin_id", targetAdmin.Id.ToString()),
            new("admin_code", targetAdmin.AdminCode ?? $"ADM-{targetAdmin.Id:D4}"),
            new("company_name", targetAdmin.CompanyName ?? ""),
            new("is_impersonating", "true")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        _logger.LogInformation("Super Admin {SuperAdminEmail} logged in as User/Company {CompanyName} ({AdminCode}).",
            superAdmin.Email, targetAdmin.CompanyName, targetAdmin.AdminCode);

        TempData["SuccessMessage"] = $"Logged in as '{targetAdmin.CompanyName ?? targetAdmin.Name}' ({targetAdmin.AdminCode}).";
        return RedirectToAction("List", "Lead");
    }

    [HttpGet]
    [Route("SuperAdmin/ExitImpersonation")]
    public async Task<IActionResult> ExitImpersonation()
    {
        Response.Cookies.Delete("ImpersonatedBySuperAdmin");
        Response.Cookies.Delete("ImpersonatedCompanyName");
        Response.Cookies.Delete("ImpersonatedAdminCode");

        var superAdmin = await _db.Users.FirstOrDefaultAsync(u => u.Role == "Super Admin")
                      ?? new UserRoleEntry { Id = 21, Name = "Super Administrator", Email = "superadmin@visioncrm.com", Role = "Super Admin" };

        var (plainToken, _) = await _tokenService.IssueTokenAsync(superAdmin.Id, "superadmin-web");
        var superAdminClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, superAdmin.Id.ToString()),
            new(ClaimTypes.Name, superAdmin.Name ?? "Super Administrator"),
            new(ClaimTypes.Email, superAdmin.Email ?? "superadmin@visioncrm.com"),
            new(ClaimTypes.Role, "Super Admin"),
            new("token_id", plainToken),
            new("admin_id", "0"),
            new("admin_code", "SAAS-ROOT"),
            new("company_name", "Vision SaaS Platform")
        };

        var identity = new ClaimsIdentity(superAdminClaims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        TempData["SuccessMessage"] = "Returned to Super Admin Control Panel.";
        return RedirectToAction(nameof(Dashboard));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. CMS (DROPDOWN: LANDING PAGE, FAQ ARTICLES, PRICING & PLANS)
    // ─────────────────────────────────────────────────────────────────────────

    // CMS -> 4.1 Landing Page
    [HttpGet]
    [Route("SuperAdmin/Cms/LandingPage")]
    public async Task<IActionResult> CmsLandingPage()
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        ViewData["Title"] = "CMS: Landing Page · SuperAdmin Panel";
        ViewData["ActivePage"] = "SuperAdminCmsLanding";

        var config = await _db.LandingPageConfigs.FirstOrDefaultAsync() ?? new CmsLandingPageConfig();
        return View(config);
    }

    [HttpPost]
    [Route("SuperAdmin/Cms/LandingPage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLandingPage(CmsLandingPageConfig model)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        var config = await _db.LandingPageConfigs.FirstOrDefaultAsync();
        if (config == null)
        {
            config = new CmsLandingPageConfig();
            _db.LandingPageConfigs.Add(config);
        }

        config.HeroTitle = model.HeroTitle?.Trim() ?? config.HeroTitle;
        config.HeroSubtitle = model.HeroSubtitle?.Trim() ?? config.HeroSubtitle;
        config.HeroBadge = model.HeroBadge?.Trim() ?? config.HeroBadge;
        config.CtaButtonText = model.CtaButtonText?.Trim() ?? config.CtaButtonText;
        config.CtaButtonUrl = model.CtaButtonUrl?.Trim() ?? config.CtaButtonUrl;
        config.FeaturesJson = model.FeaturesJson ?? config.FeaturesJson;
        config.StatsJson = model.StatsJson ?? config.StatsJson;
        config.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Landing Page content saved successfully!";
        return RedirectToAction(nameof(CmsLandingPage));
    }

    // CMS -> 4.2 FAQ Articles
    [HttpGet]
    [Route("SuperAdmin/Cms/Faqs")]
    public async Task<IActionResult> CmsFaqs()
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        ViewData["Title"] = "CMS: FAQ Articles · SuperAdmin Panel";
        ViewData["ActivePage"] = "SuperAdminCmsFaqs";

        var faqs = await _db.FaqArticles.OrderBy(f => f.Category).ThenBy(f => f.DisplayOrder).ToListAsync();
        return View(faqs);
    }

    [HttpPost]
    [Route("SuperAdmin/Cms/SaveFaq")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveFaq(int id, string question, string answer, string category, int displayOrder, bool isActive)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(answer))
        {
            TempData["ErrorMessage"] = "Question and Answer are required.";
            return RedirectToAction(nameof(CmsFaqs));
        }

        if (id == 0)
        {
            var faq = new CmsFaqArticle
            {
                Question = question.Trim(),
                Answer = answer.Trim(),
                Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(),
                DisplayOrder = displayOrder,
                IsActive = isActive,
                CreatedAt = DateTime.UtcNow
            };
            _db.FaqArticles.Add(faq);
            TempData["SuccessMessage"] = "FAQ article added.";
        }
        else
        {
            var faq = await _db.FaqArticles.FindAsync(id);
            if (faq != null)
            {
                faq.Question = question.Trim();
                faq.Answer = answer.Trim();
                faq.Category = string.IsNullOrWhiteSpace(category) ? faq.Category : category.Trim();
                faq.DisplayOrder = displayOrder;
                faq.IsActive = isActive;
                TempData["SuccessMessage"] = "FAQ article updated.";
            }
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(CmsFaqs));
    }

    [HttpPost]
    [Route("SuperAdmin/Cms/DeleteFaq")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFaq(int id)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        var faq = await _db.FaqArticles.FindAsync(id);
        if (faq != null)
        {
            _db.FaqArticles.Remove(faq);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "FAQ article deleted.";
        }
        return RedirectToAction(nameof(CmsFaqs));
    }

    // CMS -> 4.3 Pricing & Plans
    [HttpGet]
    [Route("SuperAdmin/Cms/Pricing")]
    public async Task<IActionResult> CmsPricing()
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        ViewData["Title"] = "CMS: Pricing & Plans · SuperAdmin Panel";
        ViewData["ActivePage"] = "SuperAdminCmsPricing";

        var plans = await _db.SubscriptionPlans.OrderBy(p => p.DisplayOrder).ToListAsync();
        return View(plans);
    }

    [HttpPost]
    [Route("SuperAdmin/Cms/SavePlan")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePlan(int id, string name, string code, decimal price, string billingInterval, int? maxUsers, int maxTeamMembers, int? maxLeads, int trialDays, string description, string features, bool isPopular, bool isActive, int displayOrder)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ErrorMessage"] = "Plan Name is required.";
            return RedirectToAction(nameof(CmsPricing));
        }

        var usersCap = maxUsers.HasValue && maxUsers.Value > 0 ? maxUsers.Value : (maxTeamMembers > 0 ? maxTeamMembers : 5);
        var leadsCap = maxLeads.HasValue && maxLeads.Value > 0 ? maxLeads.Value : 100;

        if (id == 0)
        {
            var plan = new SaaSSubscriptionPlan
            {
                Name = name.Trim(),
                Code = string.IsNullOrWhiteSpace(code) ? name.ToLower().Replace(" ", "-") : code.Trim().ToLower(),
                Price = Math.Max(0, price),
                BillingInterval = string.IsNullOrWhiteSpace(billingInterval) ? "Monthly" : billingInterval.Trim(),
                MaxLeads = leadsCap,
                MaxTeamMembers = usersCap,
                TrialDays = trialDays,
                Description = description?.Trim() ?? "",
                Features = features?.Trim() ?? "",
                IsPopular = isPopular,
                IsActive = isActive,
                DisplayOrder = displayOrder,
                CreatedAt = DateTime.UtcNow
            };
            _db.SubscriptionPlans.Add(plan);
            TempData["SuccessMessage"] = $"Plan '{plan.Name}' created successfully.";
        }
        else
        {
            var plan = await _db.SubscriptionPlans.FindAsync(id);
            if (plan != null)
            {
                plan.Name = name.Trim();
                plan.Code = string.IsNullOrWhiteSpace(code) ? plan.Code : code.Trim().ToLower();
                plan.Price = Math.Max(0, price);
                plan.BillingInterval = string.IsNullOrWhiteSpace(billingInterval) ? plan.BillingInterval : billingInterval.Trim();
                plan.MaxLeads = leadsCap;
                plan.MaxTeamMembers = usersCap;
                plan.TrialDays = trialDays;
                plan.Description = description?.Trim() ?? "";
                plan.Features = features?.Trim() ?? "";
                plan.IsPopular = isPopular;
                plan.IsActive = isActive;
                plan.DisplayOrder = displayOrder;
                TempData["SuccessMessage"] = $"Plan '{plan.Name}' updated successfully.";
            }
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(CmsPricing));
    }

    [HttpPost]
    [Route("SuperAdmin/Cms/TogglePlan")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePlan(int id)
    {
        var plan = await _db.SubscriptionPlans.FindAsync(id);
        if (plan != null)
        {
            plan.IsActive = !plan.IsActive;
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Plan '{plan.Name}' is now {(plan.IsActive ? "Active" : "Inactive")}.";
        }
        return RedirectToAction(nameof(CmsPricing));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. EMAIL TEMPLATES
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    [Route("SuperAdmin/EmailTemplates")]
    public async Task<IActionResult> EmailTemplates()
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        ViewData["Title"] = "Email Templates · SuperAdmin Panel";
        ViewData["ActivePage"] = "SuperAdminEmailTemplates";

        var templates = await _db.EmailTemplates.OrderBy(t => t.Id).ToListAsync();
        return View(templates);
    }

    [HttpPost]
    [Route("SuperAdmin/SaveEmailTemplate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveEmailTemplate(int id, string subject, string bodyHtml, string description)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        var tmpl = await _db.EmailTemplates.FindAsync(id);
        if (tmpl != null)
        {
            tmpl.Subject = subject?.Trim() ?? tmpl.Subject;
            tmpl.BodyHtml = bodyHtml?.Trim() ?? tmpl.BodyHtml;
            tmpl.Description = description?.Trim() ?? tmpl.Description;
            tmpl.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Email Template '{tmpl.TemplateName}' updated successfully.";
        }
        return RedirectToAction(nameof(EmailTemplates));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. SETTING
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    [Route("SuperAdmin/Settings")]
    public async Task<IActionResult> Settings()
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        ViewData["Title"] = "Platform Settings · SuperAdmin Panel";
        ViewData["ActivePage"] = "SuperAdminSettings";

        var settings = await _db.PlatformSettings.FirstOrDefaultAsync() ?? new SaaSPlatformSettings();
        return View(settings);
    }

    [HttpPost]
    [Route("SuperAdmin/SaveSettings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(SaaSPlatformSettings model)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        var settings = await _db.PlatformSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new SaaSPlatformSettings();
            _db.PlatformSettings.Add(settings);
        }

        settings.PlatformName = model.PlatformName?.Trim() ?? settings.PlatformName;
        settings.SupportEmail = model.SupportEmail?.Trim() ?? settings.SupportEmail;
        settings.SupportPhone = model.SupportPhone?.Trim() ?? settings.SupportPhone;
        settings.CurrencySymbol = string.IsNullOrWhiteSpace(model.CurrencySymbol) ? "₹" : model.CurrencySymbol.Trim();
        settings.CurrencyCode = string.IsNullOrWhiteSpace(model.CurrencyCode) ? "INR" : model.CurrencyCode.Trim();
        settings.DefaultTrialDays = model.DefaultTrialDays > 0 ? model.DefaultTrialDays : 7;
        settings.DefaultTrialMaxLeads = 999999;
        settings.RazorpayKeyId = model.RazorpayKeyId?.Trim() ?? "";
        settings.RazorpayKeySecret = model.RazorpayKeySecret?.Trim() ?? "";
        settings.RazorpayEnabled = model.RazorpayEnabled;

        // Bank Transfer Offline Payment Configuration
        settings.BankTransferEnabled = model.BankTransferEnabled;
        settings.BankName = model.BankName?.Trim() ?? settings.BankName;
        settings.BankAccountName = model.BankAccountName?.Trim() ?? settings.BankAccountName;
        settings.BankAccountNumber = model.BankAccountNumber?.Trim() ?? settings.BankAccountNumber;
        settings.BankIfscCode = model.BankIfscCode?.Trim() ?? settings.BankIfscCode;
        settings.BankBranch = model.BankBranch?.Trim() ?? settings.BankBranch;
        settings.BankUpiId = model.BankUpiId?.Trim() ?? settings.BankUpiId;
        settings.BankInstructions = model.BankInstructions?.Trim() ?? settings.BankInstructions;

        // SMTP Email Configuration
        settings.SmtpHost = model.SmtpHost?.Trim() ?? "";
        settings.SmtpPort = model.SmtpPort > 0 ? model.SmtpPort : 587;
        settings.SmtpUser = model.SmtpUser?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(model.SmtpPassword))
        {
            settings.SmtpPassword = model.SmtpPassword.Trim();
        }
        settings.SmtpFromEmail = string.IsNullOrWhiteSpace(model.SmtpFromEmail) ? "notifications@visioncrm.com" : model.SmtpFromEmail.Trim();
        settings.SmtpFromName = string.IsNullOrWhiteSpace(model.SmtpFromName) ? settings.PlatformName : model.SmtpFromName.Trim();
        settings.SmtpEnableSsl = model.SmtpEnableSsl;

        settings.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Platform & Email settings saved successfully.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost]
    [Route("SuperAdmin/SendTestEmail")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTestEmail(string recipientEmail)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        var targetEmail = !string.IsNullOrWhiteSpace(recipientEmail) ? recipientEmail.Trim() : (superAdmin.Email ?? "");
        var (success, message) = await _emailService.SendTestEmailAsync(targetEmail);

        if (success)
        {
            TempData["SuccessMessage"] = $"Test email sent successfully to '{targetEmail}'.";
        }
        else
        {
            TempData["ErrorMessage"] = $"Failed to dispatch test email: {message}";
        }

        return RedirectToAction(nameof(Settings));
    }

    // ==========================================
    // BANK TRANSFER REQUESTS MODULE
    // ==========================================

    [HttpGet]
    [Route("SuperAdmin/BankRequests")]
    [Route("SuperAdmin/BankRequest")]
    [Route("SuperAdmin/BankTransferRequests")]
    public async Task<IActionResult> BankRequests(string? status = null, string? search = null)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        ViewData["ActivePage"] = "SuperAdminBankRequests";

        var query = _db.BankTransferRequests.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "All")
        {
            query = query.Where(r => r.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(r => r.CompanyName.ToLower().Contains(s)
                || r.AdminCode.ToLower().Contains(s)
                || (r.ContactEmail != null && r.ContactEmail.ToLower().Contains(s))
                || (r.PlanName != null && r.PlanName.ToLower().Contains(s))
                || (r.TransactionReference != null && r.TransactionReference.ToLower().Contains(s)));
        }

        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

        ViewBag.SelectedStatus = status ?? "All";
        ViewBag.Search = search ?? "";
        ViewBag.TotalCount = await _db.BankTransferRequests.CountAsync();
        ViewBag.PendingCount = await _db.BankTransferRequests.CountAsync(r => r.Status == "Pending");
        ViewBag.ApprovedCount = await _db.BankTransferRequests.CountAsync(r => r.Status == "Approved");
        ViewBag.RejectedCount = await _db.BankTransferRequests.CountAsync(r => r.Status == "Rejected");

        return View("BankRequests", list);
    }

    [HttpGet]
    public async Task<IActionResult> BankRequest(string? status = null, string? search = null) => await BankRequests(status, search);

    [HttpPost]
    [Route("SuperAdmin/ApproveBankRequest")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveBankRequest(int id, string? remarks)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        var req = await _db.BankTransferRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (req == null)
        {
            TempData["ErrorMessage"] = "Bank transfer request not found.";
            return RedirectToAction(nameof(BankRequests));
        }

        if (req.Status == "Approved")
        {
            TempData["WarningMessage"] = "This bank transfer request has already been approved.";
            return RedirectToAction(nameof(BankRequests));
        }

        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == req.PlanId)
            ?? await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Name == req.PlanName);

        var sub = await _db.CompanySubscriptions.FirstOrDefaultAsync(s => s.AdminId == req.AdminId);
        DateTime startDate = DateTime.UtcNow;
        DateTime endDate = string.Equals(req.BillingInterval, "Yearly", StringComparison.OrdinalIgnoreCase) 
            ? startDate.AddYears(1) 
            : startDate.AddMonths(1);

        if (sub == null)
        {
            sub = new CompanySubscription
            {
                AdminId = req.AdminId,
                PlanId = plan?.Id ?? req.PlanId,
                PlanName = plan?.Name ?? req.PlanName,
                PlanPrice = plan?.Price ?? req.Amount,
                Status = "Active",
                PaymentStatus = "Success",
                StartDate = startDate,
                EndDate = endDate,
                PurchaseDate = DateTime.UtcNow,
                MaxUsers = plan?.MaxTeamMembers ?? 3,
                MaxLeads = plan?.MaxLeads ?? 100,
                AmountPaid = req.Amount,
                PaymentMethod = "Bank Transfer",
                Notes = $"Bank transfer #{req.Id} approved by {superAdmin.Email}"
            };
            _db.CompanySubscriptions.Add(sub);
        }
        else
        {
            sub.PlanId = plan?.Id ?? req.PlanId;
            sub.PlanName = plan?.Name ?? req.PlanName;
            sub.PlanPrice = plan?.Price ?? req.Amount;
            sub.Status = "Active";
            sub.PaymentStatus = "Success";
            sub.StartDate = startDate;
            sub.EndDate = endDate;
            sub.PurchaseDate = DateTime.UtcNow;
            sub.MaxUsers = plan?.MaxTeamMembers ?? 3;
            sub.MaxLeads = plan?.MaxLeads ?? 100;
            sub.AmountPaid = req.Amount;
            sub.PaymentMethod = "Bank Transfer";
            sub.Notes = $"Bank transfer #{req.Id} approved by {superAdmin.Email}";
        }

        req.Status = "Approved";
        req.ReviewedAt = DateTime.UtcNow;
        req.ReviewedBy = superAdmin.Email ?? "SuperAdmin";
        req.AdminRemarks = string.IsNullOrWhiteSpace(remarks) ? "Payment verified and approved by SuperAdmin." : remarks.Trim();

        // Record in SubscriptionPayments history
        var paymentRecord = new SubscriptionPaymentRecord
        {
            AdminId = req.AdminId,
            CompanyName = req.CompanyName,
            AdminCode = req.AdminCode,
            PlanId = plan?.Id ?? req.PlanId,
            PlanName = plan?.Name ?? req.PlanName,
            BillingInterval = req.BillingInterval,
            Amount = req.Amount,
            PaymentMethod = "Bank Transfer",
            TransactionId = req.TransactionReference,
            OrderId = $"BANK-REQ-#{req.Id}",
            Status = "Success",
            ReceiptUrl = req.ReceiptFilePath,
            Notes = $"Bank transfer #{req.Id} approved by {superAdmin.Email}. {remarks}",
            CreatedAt = DateTime.UtcNow
        };
        _db.SubscriptionPayments.Add(paymentRecord);

        await _db.SaveChangesAsync();

        // Send activation email
        try
        {
            var companyUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.AdminId);
            if (companyUser != null && !string.IsNullOrWhiteSpace(companyUser.Email))
            {
                var placeholders = new Dictionary<string, string>
                {
                    { "{CompanyName}", companyUser.CompanyName ?? req.CompanyName },
                    { "{AdminName}", companyUser.Name ?? "" },
                    { "{AdminCode}", companyUser.AdminCode ?? req.AdminCode },
                    { "{PlanName}", req.PlanName },
                    { "{LoginUrl}", $"{Request.Scheme}://{Request.Host}/Auth/Login" },
                    { "{DaysLeft}", string.Equals(req.BillingInterval, "Yearly", StringComparison.OrdinalIgnoreCase) ? "365" : "30" }
                };
                await _emailService.SendTemplatedEmailAsync(
                    "PLAN_ACTIVATED",
                    companyUser.Email,
                    companyUser.Name ?? companyUser.CompanyName ?? "Customer",
                    placeholders);
            }
        }
        catch { }

        TempData["SuccessMessage"] = $"✅ Bank Request #{req.Id} for {req.CompanyName} ({req.AdminCode}) approved! Plan {req.PlanName} is now active until {endDate:dd MMM yyyy}.";
        return RedirectToAction(nameof(BankRequests));
    }

    [HttpPost]
    [Route("SuperAdmin/RejectBankRequest")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectBankRequest(int id, string? remarks)
    {
        var superAdmin = GetCurrentSuperAdmin();
        if (superAdmin == null) return RedirectToAction(nameof(Dashboard));

        var req = await _db.BankTransferRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (req == null)
        {
            TempData["ErrorMessage"] = "Bank transfer request not found.";
            return RedirectToAction(nameof(BankRequests));
        }

        req.Status = "Rejected";
        req.ReviewedAt = DateTime.UtcNow;
        req.ReviewedBy = superAdmin.Email ?? "SuperAdmin";
        req.AdminRemarks = string.IsNullOrWhiteSpace(remarks) ? "Payment receipt verification failed." : remarks.Trim();

        var pendingRecord = await _db.SubscriptionPayments
            .FirstOrDefaultAsync(p => p.AdminId == req.AdminId && p.Status == "Pending");
        if (pendingRecord != null)
        {
            pendingRecord.Status = "Rejected";
            pendingRecord.Notes = $"Bank transfer #{req.Id} rejected by {superAdmin.Email}. {remarks}";
        }

        await _db.SaveChangesAsync();

        TempData["WarningMessage"] = $"❌ Bank Request #{req.Id} for {req.CompanyName} marked as Rejected.";
        return RedirectToAction(nameof(BankRequests));
    }
}
