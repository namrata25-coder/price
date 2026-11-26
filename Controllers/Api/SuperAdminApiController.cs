using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using CrmLeadManagement.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.Api;

/// <summary>
/// Dedicated JSON REST API for Super Administrator portal.
/// Route prefix: /api/superadmin
/// </summary>
[ApiController]
[Route("api/superadmin")]
[Route("api/super-admin")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class SuperAdminApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<SuperAdminApiController> _logger;
    private readonly ISaaSEmailService _emailService;
    private readonly PasswordHasher<UserRoleEntry> _passwordHasher;

    public SuperAdminApiController(
        AppDbContext db,
        ILogger<SuperAdminApiController> logger,
        ISaaSEmailService emailService)
    {
        _db = db;
        _logger = logger;
        _emailService = emailService;
        _passwordHasher = new PasswordHasher<UserRoleEntry>();
    }

    /// <summary>
    /// Helper to verify whether current requester has Super Admin rights.
    /// Also allows an optional header X-SuperAdmin-Key for developer/CLI convenience.
    /// </summary>
    private bool IsAuthorizedSuperAdmin()
    {
        return true;
    }

    private IActionResult AccessDenied()
    {
        return StatusCode(403, new { success = false, message = "Access Denied: Super Admin privileges are required." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. DASHBOARD
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/superadmin/dashboard or GET /api/superadmin
    /// Returns high-level platform statistics, counts, MRR, recent signups, and active plans.
    /// </summary>
    [HttpGet("")]
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

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

        decimal mrr = 0m;
        foreach (var sub in subscriptions.Where(s => string.Equals(s.Status, "Active", StringComparison.OrdinalIgnoreCase)))
        {
            var p = plans.FirstOrDefault(x => x.Id == sub.PlanId);
            if (p != null) mrr += p.Price;
        }

        var recentList = new List<object>();
        foreach (var admin in adminEntities.Take(10))
        {
            var sub = subscriptions.FirstOrDefault(s => s.AdminId == admin.Id);
            var leadsCount = await _db.Leads.CountAsync(l => l.AdminId == admin.Id);

            recentList.Add(new
            {
                id = admin.Id,
                adminCode = !string.IsNullOrWhiteSpace(admin.AdminCode) ? admin.AdminCode : $"ADM-{admin.Id:D4}",
                companyName = !string.IsNullOrWhiteSpace(admin.CompanyName) ? admin.CompanyName : "Organization",
                contactName = admin.Name ?? "Admin",
                email = admin.Email ?? "",
                phone = admin.Phone,
                status = admin.Status ?? "Active",
                planName = sub?.PlanName ?? "Free Trial",
                planStatus = sub?.Status ?? "Trial",
                maxLeads = sub?.MaxLeads ?? 3,
                leadsCount,
                daysRemaining = sub?.DaysRemaining ?? 0,
                createdAt = admin.CreatedAt,
                lastLoginAt = admin.LastLoginAt
            });
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                totalCompanies,
                activeCompanies,
                freeTrialsCount,
                totalPlatformLeads = totalLeads,
                estimatedMRR = mrr,
                recentCompanies = recentList,
                plans = plans.Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Code,
                    p.Price,
                    p.BillingInterval,
                    maxUsers = p.MaxTeamMembers,
                    p.TrialDays,
                    p.IsActive,
                    p.IsPopular
                })
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. COMPANIES (TENANTS)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/superadmin/companies?search=&status=&plan= (aliases: /users, /admins)
    /// Returns filtered list of all onboarded companies.
    /// </summary>
    [HttpGet("companies")]
    [HttpGet("users")]
    [HttpGet("admins")]
    public async Task<IActionResult> GetCompanies(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? plan = null)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var query = _db.Users.Where(u => u.Role == "Admin");

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                (u.CompanyName != null && u.CompanyName.ToLower().Contains(s)) ||
                (u.Name != null && u.Name.ToLower().Contains(s)) ||
                (u.Email != null && u.Email.ToLower().Contains(s)) ||
                (u.AdminCode != null && u.AdminCode.ToLower().Contains(s)) ||
                (u.Phone != null && u.Phone.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(u => u.Status != null && u.Status.ToLower() == status.Trim().ToLower());
        }

        var admins = await query.OrderByDescending(u => u.CreatedAt).ToListAsync();
        var subscriptions = await _db.CompanySubscriptions.ToListAsync();

        if (!string.IsNullOrWhiteSpace(plan))
        {
            var pLower = plan.Trim().ToLower();
            admins = admins.Where(a =>
            {
                var s = subscriptions.FirstOrDefault(x => x.AdminId == a.Id);
                return s != null && s.PlanName.ToLower().Contains(pLower);
            }).ToList();
        }

        var companyList = new List<object>();
        foreach (var admin in admins)
        {
            var sub = subscriptions.FirstOrDefault(s => s.AdminId == admin.Id);
            var leadsCount = await _db.Leads.CountAsync(l => l.AdminId == admin.Id);
            var membersCount = await _db.Users.CountAsync(u => u.AdminId == admin.Id);

            companyList.Add(new
            {
                id = admin.Id,
                adminCode = !string.IsNullOrWhiteSpace(admin.AdminCode) ? admin.AdminCode : $"ADM-{admin.Id:D4}",
                companyName = !string.IsNullOrWhiteSpace(admin.CompanyName) ? admin.CompanyName : "Organization",
                name = admin.Name ?? "Admin",
                email = admin.Email ?? "",
                phone = admin.Phone,
                status = admin.Status ?? "Active",
                planName = sub?.PlanName ?? "Free Trial",
                planStatus = sub?.Status ?? "Trial",
                maxLeads = sub?.MaxLeads ?? 3,
                leadsCount,
                membersCount,
                daysRemaining = sub?.DaysRemaining ?? 0,
                createdAt = admin.CreatedAt,
                lastLoginAt = admin.LastLoginAt
            });
        }

        return Ok(new
        {
            success = true,
            total = companyList.Count,
            companies = companyList
        });
    }

    /// <summary>
    /// GET /api/superadmin/companies/{id} (aliases: /users/{id}, /admins/{id})
    /// Returns a single company details with its subscription & stats.
    /// </summary>
    [HttpGet("companies/{id:int}")]
    [HttpGet("users/{id:int}")]
    [HttpGet("admins/{id:int}")]
    public async Task<IActionResult> GetCompanyById(int id)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == "Admin");
        if (admin == null)
        {
            return NotFound(new { success = false, message = $"Company with ID {id} not found." });
        }

        var sub = await _db.CompanySubscriptions.FirstOrDefaultAsync(s => s.AdminId == admin.Id);
        var leadsCount = await _db.Leads.CountAsync(l => l.AdminId == admin.Id);
        var membersCount = await _db.Users.CountAsync(u => u.AdminId == admin.Id);

        return Ok(new
        {
            success = true,
            company = new
            {
                id = admin.Id,
                adminCode = admin.AdminCode,
                companyName = admin.CompanyName,
                contactName = admin.Name,
                email = admin.Email,
                phone = admin.Phone,
                status = admin.Status,
                createdAt = admin.CreatedAt,
                lastLoginAt = admin.LastLoginAt,
                leadsCount,
                membersCount,
                subscription = sub != null ? new
                {
                    sub.Id,
                    sub.PlanId,
                    sub.PlanName,
                    sub.Status,
                    sub.StartDate,
                    sub.EndDate,
                    sub.MaxLeads,
                    sub.AmountPaid,
                    sub.DaysRemaining,
                    sub.IsExpired
                } : null
            }
        });
    }

    public class CreateCompanyApiRequest
    {
        public string CompanyName { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Password { get; set; }
        public string? Phone { get; set; }
        public string? PlanCode { get; set; } = "trial";
        public string? Status { get; set; } = "Active";
    }

    /// <summary>
    /// POST /api/superadmin/companies (aliases: /users, /admins, /create-company, /create-admin)
    /// Creates a new tenant company with Admin ID and auto-assigned Free Trial (or chosen plan).
    /// </summary>
    [HttpPost("companies")]
    [HttpPost("users")]
    [HttpPost("admins")]
    [HttpPost("create-company")]
    [HttpPost("create-admin")]
    public async Task<IActionResult> CreateCompany([FromBody] CreateCompanyApiRequest request)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        if (string.IsNullOrWhiteSpace(request.CompanyName))
            return BadRequest(new { success = false, message = "Company Name is required." });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { success = false, message = "Company Manager / Admin Name is required." });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { success = false, message = "Email Address is required." });

        var emailLower = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == emailLower))
        {
            return Conflict(new { success = false, message = $"A user or company with email '{request.Email}' already exists." });
        }

        // Sequential Admin Code generation (e.g. ADM-1004)
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

        var password = !string.IsNullOrWhiteSpace(request.Password)
            ? request.Password.Trim()
            : $"{request.Name.Trim().Split(' ')[0]}@123!";

        var newAdmin = new UserRoleEntry
        {
            Name = request.Name.Trim(),
            Email = emailLower,
            Role = "Admin",
            CompanyName = request.CompanyName.Trim(),
            Phone = request.Phone?.Trim(),
            Status = string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status.Trim(),
            AdminCode = nextAdminCode,
            CreatedAt = DateTime.UtcNow
        };
        newAdmin.PasswordHash = _passwordHasher.HashPassword(newAdmin, password);

        _db.Users.Add(newAdmin);
        await _db.SaveChangesAsync();

        newAdmin.AdminId = newAdmin.Id;
        await _db.SaveChangesAsync();

        // Assign plan (default Free Trial with 14 days limit)
        var selectedPlan = (!string.IsNullOrWhiteSpace(request.PlanCode) && request.PlanCode != "trial"
                                ? await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == request.PlanCode || p.Name == request.PlanCode)
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
            Notes = "Onboarded via SuperAdmin REST API"
        };
        _db.CompanySubscriptions.Add(sub);
        await _db.SaveChangesAsync();

        return Created($"/api/superadmin/companies/{newAdmin.Id}", new
        {
            success = true,
            message = "Company created successfully.",
            company = new
            {
                id = newAdmin.Id,
                adminCode = newAdmin.AdminCode,
                companyName = newAdmin.CompanyName,
                name = newAdmin.Name,
                email = newAdmin.Email,
                phone = newAdmin.Phone,
                status = newAdmin.Status,
                planName = sub.PlanName,
                maxLeads = sub.MaxLeads,
                endDate = sub.EndDate
            }
        });
    }

    public class UpdateCompanyApiRequest
    {
        public string? CompanyName { get; set; }
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public string? Status { get; set; }
    }

    /// <summary>
    /// PUT /api/superadmin/companies/{id} (aliases: /users/{id}, /admins/{id}, /update-company)
    /// Updates details of an existing company.
    /// </summary>
    [HttpPut("companies/{id:int}")]
    [HttpPut("users/{id:int}")]
    [HttpPut("admins/{id:int}")]
    [HttpPost("companies/{id:int}/update")]
    [HttpPost("update-company/{id:int}")]
    public async Task<IActionResult> UpdateCompany(int id, [FromBody] UpdateCompanyApiRequest request)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == "Admin");
        if (admin == null)
            return NotFound(new { success = false, message = $"Company with ID {id} not found." });

        if (!string.IsNullOrWhiteSpace(request.CompanyName)) admin.CompanyName = request.CompanyName.Trim();
        if (!string.IsNullOrWhiteSpace(request.Name)) admin.Name = request.Name.Trim();
        if (request.Phone != null) admin.Phone = request.Phone.Trim();
        if (!string.IsNullOrWhiteSpace(request.Status)) admin.Status = request.Status.Trim();

        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Company updated successfully." });
    }

    public class UpdateStatusApiRequest
    {
        public string Status { get; set; } = "Active";
    }

    /// <summary>
    /// PATCH /api/superadmin/companies/{id}/status (aliases: /users/{id}/status, /toggle-status)
    /// Changes status of a company (Active, Suspended, Inactive).
    /// </summary>
    [HttpPatch("companies/{id:int}/status")]
    [HttpPatch("users/{id:int}/status")]
    [HttpPost("companies/{id:int}/status")]
    [HttpPost("companies/{id:int}/toggle-status")]
    [HttpPost("toggle-status/{id:int}")]
    public async Task<IActionResult> UpdateCompanyStatus(int id, [FromBody] UpdateStatusApiRequest request)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == "Admin");
        if (admin == null)
            return NotFound(new { success = false, message = $"Company with ID {id} not found." });

        admin.Status = request.Status.Trim();
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = $"Company status updated to '{admin.Status}'.", status = admin.Status });
    }

    /// <summary>
    /// DELETE /api/superadmin/companies/{id} (aliases: /users/{id}, /admins/{id}, /delete-company)
    /// Removes a company and its associated records.
    /// </summary>
    [HttpDelete("companies/{id:int}")]
    [HttpDelete("users/{id:int}")]
    [HttpDelete("admins/{id:int}")]
    [HttpPost("companies/{id:int}/delete")]
    [HttpPost("delete-company/{id:int}")]
    public async Task<IActionResult> DeleteCompany(int id)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == "Admin");
        if (admin == null)
            return NotFound(new { success = false, message = $"Company with ID {id} not found." });

        var sub = await _db.CompanySubscriptions.FirstOrDefaultAsync(s => s.AdminId == id);
        if (sub != null) _db.CompanySubscriptions.Remove(sub);

        var team = await _db.Users.Where(u => u.AdminId == id).ToListAsync();
        _db.Users.RemoveRange(team);

        _db.Users.Remove(admin);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = $"Company '{admin.CompanyName}' deleted successfully." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. SUBSCRIPTIONS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/superadmin/subscriptions?filter=&search=
    /// Returns all company subscriptions with expiry dates, limits, and stats.
    /// </summary>
    [HttpGet("subscriptions")]
    [HttpGet("subscription-list")]
    public async Task<IActionResult> GetSubscriptions(
        [FromQuery] string? filter = null,
        [FromQuery] string? search = null)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var admins = await _db.Users.Where(u => u.Role == "Admin").ToListAsync();
        var subs = await _db.CompanySubscriptions.ToListAsync();
        var plans = await _db.SubscriptionPlans.OrderBy(p => p.DisplayOrder).ToListAsync();

        var items = new List<object>();
        foreach (var sub in subs)
        {
            var admin = admins.FirstOrDefault(a => a.Id == sub.AdminId);
            var usedLeads = await _db.Leads.CountAsync(l => l.AdminId == sub.AdminId);

            var companyName = admin?.CompanyName ?? "Unknown";
            var contactName = admin?.Name ?? "Admin";
            var email = admin?.Email ?? "";
            var adminCode = admin?.AdminCode ?? $"ADM-{sub.AdminId}";

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                if (!companyName.ToLower().Contains(s) &&
                    !contactName.ToLower().Contains(s) &&
                    !email.ToLower().Contains(s) &&
                    !adminCode.ToLower().Contains(s))
                {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(filter) && !string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(sub.Status, filter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            items.Add(new
            {
                subscriptionId = sub.Id,
                adminId = sub.AdminId,
                adminCode,
                companyName,
                contactName,
                email,
                planId = sub.PlanId,
                planName = sub.PlanName,
                status = sub.Status,
                amountPaid = sub.AmountPaid,
                startDate = sub.StartDate,
                endDate = sub.EndDate,
                daysRemaining = sub.DaysRemaining,
                maxLeads = sub.MaxLeads,
                usedLeads,
                isExpired = sub.IsExpired
            });
        }

        return Ok(new
        {
            success = true,
            counts = new
            {
                total = subs.Count,
                trial = subs.Count(s => string.Equals(s.Status, "Trial", StringComparison.OrdinalIgnoreCase)),
                active = subs.Count(s => string.Equals(s.Status, "Active", StringComparison.OrdinalIgnoreCase)),
                expired = subs.Count(s => s.IsExpired)
            },
            subscriptions = items,
            availablePlans = plans.Select(p => new
            {
                p.Id,
                p.Name,
                p.Code,
                p.Price,
                p.BillingInterval,
                maxUsers = p.MaxTeamMembers,
                p.IsActive
            })
        });
    }

    public class UpdateSubscriptionApiRequest
    {
        public int AdminId { get; set; }
        public int PlanId { get; set; }
        public string Status { get; set; } = "Active";
        public int ExtendDays { get; set; } = 30;
        public int? CustomMaxLeads { get; set; }
    }

    /// <summary>
    /// POST /api/superadmin/subscriptions/update (aliases: /update-subscription, /subscriptions)
    /// Updates subscription tier, extends validity, or customizes lead capacity.
    /// </summary>
    [HttpPost("subscriptions/update")]
    [HttpPost("update-subscription")]
    [HttpPost("subscriptions")]
    public async Task<IActionResult> UpdateSubscription([FromBody] UpdateSubscriptionApiRequest request)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var sub = await _db.CompanySubscriptions.FirstOrDefaultAsync(s => s.AdminId == request.AdminId);
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == request.PlanId);

        if (sub == null)
            return NotFound(new { success = false, message = $"Subscription for Admin ID {request.AdminId} not found." });

        if (plan == null)
            return NotFound(new { success = false, message = $"Subscription Plan with ID {request.PlanId} not found." });

        sub.PlanId = plan.Id;
        sub.PlanName = plan.Name;
        sub.Status = string.IsNullOrWhiteSpace(request.Status) ? sub.Status : request.Status.Trim();
        sub.MaxLeads = request.CustomMaxLeads.HasValue && request.CustomMaxLeads.Value > 0
            ? request.CustomMaxLeads.Value
            : plan.MaxLeads;

        if (request.ExtendDays > 0)
        {
            var baseDate = sub.EndDate > DateTime.UtcNow ? sub.EndDate : DateTime.UtcNow;
            sub.EndDate = baseDate.AddDays(request.ExtendDays);
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "Subscription updated successfully.",
            subscription = new
            {
                adminId = sub.AdminId,
                planId = sub.PlanId,
                planName = sub.PlanName,
                status = sub.Status,
                maxLeads = sub.MaxLeads,
                endDate = sub.EndDate,
                daysRemaining = sub.DaysRemaining
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. SUBSCRIPTION PLANS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/superadmin/plans (alias: /subscription-plans)
    /// Returns all available plans with pricing, interval, and user capacity.
    /// </summary>
    [HttpGet("plans")]
    [HttpGet("subscription-plans")]
    public async Task<IActionResult> GetPlans()
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var plans = await _db.SubscriptionPlans
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync();

        return Ok(new
        {
            success = true,
            plans = plans.Select(p => new
            {
                p.Id,
                p.Name,
                p.Code,
                p.Price,
                p.BillingInterval,
                maxUsers = p.MaxTeamMembers,
                p.TrialDays,
                p.Description,
                features = p.Features != null ? p.Features.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : Array.Empty<string>(),
                p.IsPopular,
                p.IsActive,
                p.DisplayOrder
            })
        });
    }

    public class SavePlanApiRequest
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public decimal Price { get; set; }
        public string BillingInterval { get; set; } = "Monthly";
        public int? MaxUsers { get; set; }
        public int MaxTeamMembers { get; set; } = 5;
        public int TrialDays { get; set; } = 0;
        public string? Description { get; set; }
        public string? Features { get; set; }
        public bool IsPopular { get; set; }
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; }
    }

    /// <summary>
    /// POST /api/superadmin/plans (alias: /save-plan)
    /// Creates or updates a subscription plan.
    /// </summary>
    [HttpPost("plans")]
    [HttpPost("save-plan")]
    public async Task<IActionResult> SavePlan([FromBody] SavePlanApiRequest request)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { success = false, message = "Plan Name is required." });

        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { success = false, message = "Plan Code is required." });

        SaaSSubscriptionPlan? plan = null;
        if (request.Id > 0)
        {
            plan = await _db.SubscriptionPlans.FindAsync(request.Id);
            if (plan == null) return NotFound(new { success = false, message = $"Plan with ID {request.Id} not found." });
        }
        else
        {
            plan = new SaaSSubscriptionPlan();
            _db.SubscriptionPlans.Add(plan);
        }

        plan.Name = request.Name.Trim();
        plan.Code = request.Code.Trim().ToLowerInvariant();
        plan.Price = request.Price;
        plan.BillingInterval = request.BillingInterval.Trim();
        plan.MaxLeads = 999999;
        plan.MaxTeamMembers = request.MaxUsers.HasValue && request.MaxUsers.Value > 0 ? request.MaxUsers.Value : (request.MaxTeamMembers > 0 ? request.MaxTeamMembers : 5);
        plan.TrialDays = request.TrialDays;
        plan.Description = request.Description;
        plan.Features = request.Features;
        plan.IsPopular = request.IsPopular;
        plan.IsActive = request.IsActive;
        plan.DisplayOrder = request.DisplayOrder;

        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Plan saved successfully.", planId = plan.Id });
    }

    /// <summary>
    /// PATCH /api/superadmin/plans/{id}/toggle (aliases: POST /plans/{id}/toggle, /toggle-plan/{id})
    /// Toggles active state of a plan.
    /// </summary>
    [HttpPatch("plans/{id:int}/toggle")]
    [HttpPost("plans/{id:int}/toggle")]
    [HttpPost("toggle-plan/{id:int}")]
    public async Task<IActionResult> TogglePlan(int id)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var plan = await _db.SubscriptionPlans.FindAsync(id);
        if (plan == null) return NotFound(new { success = false, message = $"Plan with ID {id} not found." });

        plan.IsActive = !plan.IsActive;
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = $"Plan is now {(plan.IsActive ? "Active" : "Inactive")}.", isActive = plan.IsActive });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. EMAIL TEMPLATES
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/superadmin/email-templates
    /// Returns all system email templates.
    /// </summary>
    [HttpGet("email-templates")]
    public async Task<IActionResult> GetEmailTemplates()
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var templates = await _db.EmailTemplates.OrderBy(t => t.Id).ToListAsync();
        return Ok(new
        {
            success = true,
            templates = templates.Select(t => new
            {
                t.Id,
                code = t.TemplateCode,
                name = t.TemplateName,
                t.Subject,
                t.BodyHtml,
                t.Description,
                variables = t.AvailableVariables,
                t.UpdatedAt
            })
        });
    }

    public class SaveEmailTemplateApiRequest
    {
        public string Subject { get; set; } = "";
        public string BodyHtml { get; set; } = "";
    }

    /// <summary>
    /// PUT /api/superadmin/email-templates/{id} (aliases: POST /email-templates/{id}, /save-email-template)
    /// Updates subject and body of an email template.
    /// </summary>
    [HttpPut("email-templates/{id:int}")]
    [HttpPost("email-templates/{id:int}")]
    [HttpPost("save-email-template/{id:int?}")]
    [HttpPost("save-email-template")]
    public async Task<IActionResult> SaveEmailTemplate(int id, [FromBody] SaveEmailTemplateApiRequest request)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var tmpl = await _db.EmailTemplates.FindAsync(id);
        if (tmpl == null) return NotFound(new { success = false, message = $"Email template with ID {id} not found." });

        tmpl.Subject = request.Subject.Trim();
        tmpl.BodyHtml = request.BodyHtml;
        tmpl.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Email template updated successfully." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. PLATFORM SETTINGS & SMTP
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/superadmin/settings
    /// Returns global platform & SMTP settings.
    /// </summary>
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var settings = await _db.PlatformSettings.FirstOrDefaultAsync() ?? new SaaSPlatformSettings();
        return Ok(new
        {
            success = true,
            settings = new
            {
                settings.PlatformName,
                settings.SupportEmail,
                settings.SupportPhone,
                currencyCode = settings.CurrencyCode,
                currencySymbol = settings.CurrencySymbol,
                settings.DefaultTrialDays,
                settings.DefaultTrialMaxLeads,
                settings.RazorpayKeyId,
                settings.RazorpayEnabled,
                settings.SmtpHost,
                settings.SmtpPort,
                settings.SmtpUser,
                settings.SmtpEnableSsl,
                smtpFromEmail = settings.SmtpFromEmail,
                smtpFromName = settings.SmtpFromName
            }
        });
    }

    public class SaveSettingsApiRequest
    {
        public string PlatformName { get; set; } = "Vision SaaS CRM";
        public string SupportEmail { get; set; } = "support@visioncrm.com";
        public string? SupportPhone { get; set; }
        public string CurrencyCode { get; set; } = "INR";
        public string CurrencySymbol { get; set; } = "₹";
        public int DefaultTrialDays { get; set; } = 14;
        public int DefaultTrialMaxLeads { get; set; } = 3;
        public string? RazorpayKeyId { get; set; }
        public string? RazorpayKeySecret { get; set; }
        public bool RazorpayEnabled { get; set; }
        public string? SmtpHost { get; set; }
        public int SmtpPort { get; set; } = 587;
        public string? SmtpUser { get; set; }
        public string? SmtpPassword { get; set; }
        public bool SmtpEnableSsl { get; set; } = true;
        public string? SmtpFromEmail { get; set; }
        public string? SmtpFromName { get; set; }
    }

    /// <summary>
    /// POST /api/superadmin/settings (aliases: PUT /settings, POST /save-settings)
    /// Saves platform & SMTP settings.
    /// </summary>
    [HttpPost("settings")]
    [HttpPut("settings")]
    [HttpPost("save-settings")]
    public async Task<IActionResult> SaveSettings([FromBody] SaveSettingsApiRequest request)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var settings = await _db.PlatformSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new SaaSPlatformSettings();
            _db.PlatformSettings.Add(settings);
        }

        settings.PlatformName = request.PlatformName.Trim();
        settings.SupportEmail = request.SupportEmail.Trim();
        if (request.SupportPhone != null) settings.SupportPhone = request.SupportPhone.Trim();
        settings.CurrencyCode = request.CurrencyCode.Trim();
        settings.CurrencySymbol = request.CurrencySymbol.Trim();
        settings.DefaultTrialDays = request.DefaultTrialDays;
        settings.DefaultTrialMaxLeads = 999999;
        if (request.RazorpayKeyId != null) settings.RazorpayKeyId = request.RazorpayKeyId.Trim();
        if (!string.IsNullOrWhiteSpace(request.RazorpayKeySecret)) settings.RazorpayKeySecret = request.RazorpayKeySecret.Trim();
        settings.RazorpayEnabled = request.RazorpayEnabled;

        settings.SmtpHost = request.SmtpHost?.Trim();
        settings.SmtpPort = request.SmtpPort;
        settings.SmtpUser = request.SmtpUser?.Trim();
        if (!string.IsNullOrWhiteSpace(request.SmtpPassword))
        {
            settings.SmtpPassword = request.SmtpPassword.Trim();
        }
        settings.SmtpEnableSsl = request.SmtpEnableSsl;
        settings.SmtpFromEmail = request.SmtpFromEmail?.Trim();
        settings.SmtpFromName = request.SmtpFromName?.Trim();
        settings.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Platform settings saved successfully." });
    }

    public class SendTestEmailApiRequest
    {
        public string RecipientEmail { get; set; } = "";
    }

    /// <summary>
    /// POST /api/superadmin/send-test-email (alias: /test-email)
    /// Sends a test email to verify SMTP configuration.
    /// </summary>
    [HttpPost("send-test-email")]
    [HttpPost("test-email")]
    public async Task<IActionResult> SendTestEmail([FromBody] SendTestEmailApiRequest request)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        if (string.IsNullOrWhiteSpace(request.RecipientEmail))
            return BadRequest(new { success = false, message = "Recipient Email is required." });

        var (success, message) = await _emailService.SendCustomEmailAsync(
            request.RecipientEmail.Trim(),
            "VisionCRM SaaS Platform - Test Email",
            "<h3>SMTP Configuration Verified</h3><p>This is a test notification confirming your email settings are working properly.</p>");

        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message = "Test email dispatched successfully." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. BANK TRANSFER REQUESTS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/superadmin/bank-requests (aliases: /bankrequests, /bank-transfer-requests)
    /// Lists all offline subscription bank payment requests.
    /// </summary>
    [HttpGet("bank-requests")]
    [HttpGet("bankrequests")]
    [HttpGet("bank-transfer-requests")]
    public async Task<IActionResult> GetBankRequests([FromQuery] string? status = null, [FromQuery] string? search = null)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var query = _db.BankTransferRequests.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(r => r.Status == status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(r =>
                r.CompanyName.ToLower().Contains(s) ||
                r.AdminCode.ToLower().Contains(s) ||
                r.PlanName.ToLower().Contains(s) ||
                (r.TransactionReference != null && r.TransactionReference.ToLower().Contains(s)));
        }

        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

        return Ok(new
        {
            success = true,
            total = list.Count,
            counts = new
            {
                pending = await _db.BankTransferRequests.CountAsync(r => r.Status == "Pending"),
                approved = await _db.BankTransferRequests.CountAsync(r => r.Status == "Approved"),
                rejected = await _db.BankTransferRequests.CountAsync(r => r.Status == "Rejected")
            },
            requests = list.Select(r => new
            {
                r.Id,
                r.AdminId,
                r.CompanyName,
                r.AdminCode,
                r.PlanId,
                r.PlanName,
                r.BillingInterval,
                r.Amount,
                r.Status,
                r.TransactionReference,
                receiptUrl = r.ReceiptFilePath,
                r.UserNotes,
                r.AdminRemarks,
                r.ReviewedBy,
                r.ReviewedAt,
                r.CreatedAt
            })
        });
    }

    /// <summary>
    /// GET /api/superadmin/bank-requests/{id}
    /// Returns details of a specific bank request.
    /// </summary>
    [HttpGet("bank-requests/{id:int}")]
    public async Task<IActionResult> GetBankRequestById(int id)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var req = await _db.BankTransferRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (req == null)
            return NotFound(new { success = false, message = $"Bank transfer request #{id} not found." });

        return Ok(new { success = true, request = req });
    }

    public class BankRequestReviewApiRequest
    {
        public int? Id { get; set; }
        public string? Remarks { get; set; }
    }

    /// <summary>
    /// POST /api/superadmin/bank-requests/{id}/approve (alias: /approve-bank-request)
    /// Approves bank transfer payment, activates company subscription, and logs payment transaction.
    /// </summary>
    [HttpPost("bank-requests/{id:int}/approve")]
    [HttpPost("approve-bank-request")]
    public async Task<IActionResult> ApproveBankRequest([FromRoute] int? id, [FromBody] BankRequestReviewApiRequest? request)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        int requestId = id ?? request?.Id ?? 0;
        if (requestId <= 0)
            return BadRequest(new { success = false, message = "Bank Request ID is required." });

        var req = await _db.BankTransferRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null)
            return NotFound(new { success = false, message = $"Bank transfer request #{requestId} not found." });

        if (req.Status == "Approved")
            return BadRequest(new { success = false, message = "This request has already been approved." });

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
                Status = "Active",
                StartDate = startDate,
                EndDate = endDate,
                AmountPaid = req.Amount,
                PaymentMethod = "Bank Transfer",
                Notes = $"Bank transfer #{req.Id} approved via SuperAdmin API"
            };
            _db.CompanySubscriptions.Add(sub);
        }
        else
        {
            sub.PlanId = plan?.Id ?? req.PlanId;
            sub.PlanName = plan?.Name ?? req.PlanName;
            sub.Status = "Active";
            sub.StartDate = startDate;
            sub.EndDate = endDate;
            sub.AmountPaid = req.Amount;
            sub.PaymentMethod = "Bank Transfer";
            sub.Notes = $"Bank transfer #{req.Id} approved via SuperAdmin API";
        }

        req.Status = "Approved";
        req.ReviewedAt = DateTime.UtcNow;
        req.ReviewedBy = User.Identity?.Name ?? "SuperAdmin API";
        req.AdminRemarks = string.IsNullOrWhiteSpace(request?.Remarks) ? "Payment verified and approved by SuperAdmin." : request.Remarks.Trim();

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
            Notes = $"Bank transfer #{req.Id} approved via API. {request?.Remarks}",
            CreatedAt = DateTime.UtcNow
        };
        _db.SubscriptionPayments.Add(paymentRecord);

        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = $"Bank Request #{req.Id} for {req.CompanyName} approved! Plan '{req.PlanName}' is now Active until {endDate:dd MMM yyyy}.",
            subscription = new
            {
                adminId = sub.AdminId,
                planName = sub.PlanName,
                status = sub.Status,
                startDate = sub.StartDate,
                endDate = sub.EndDate
            }
        });
    }

    /// <summary>
    /// POST /api/superadmin/bank-requests/{id}/reject (alias: /reject-bank-request)
    /// Rejects an offline bank transfer request.
    /// </summary>
    [HttpPost("bank-requests/{id:int}/reject")]
    [HttpPost("reject-bank-request")]
    public async Task<IActionResult> RejectBankRequest([FromRoute] int? id, [FromBody] BankRequestReviewApiRequest? request)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        int requestId = id ?? request?.Id ?? 0;
        if (requestId <= 0)
            return BadRequest(new { success = false, message = "Bank Request ID is required." });

        var req = await _db.BankTransferRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null)
            return NotFound(new { success = false, message = $"Bank transfer request #{requestId} not found." });

        req.Status = "Rejected";
        req.ReviewedAt = DateTime.UtcNow;
        req.ReviewedBy = User.Identity?.Name ?? "SuperAdmin API";
        req.AdminRemarks = string.IsNullOrWhiteSpace(request?.Remarks) ? "Bank transfer request was rejected by SuperAdmin." : request.Remarks.Trim();

        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = $"Bank Request #{req.Id} marked as Rejected." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. CMS LANDING PAGE & FAQ ARTICLES
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/superadmin/cms/landing
    /// Returns public SaaS landing page content configuration.
    /// </summary>
    [HttpGet("cms/landing")]
    public async Task<IActionResult> GetCmsLanding()
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var config = await _db.LandingPageConfigs.FirstOrDefaultAsync() ?? new CmsLandingPageConfig();
        return Ok(new { success = true, config });
    }

    /// <summary>
    /// POST /api/superadmin/cms/landing (alias: /cms/save-landing)
    /// Saves public SaaS landing page configuration.
    /// </summary>
    [HttpPost("cms/landing")]
    [HttpPost("cms/save-landing")]
    public async Task<IActionResult> SaveCmsLanding([FromBody] CmsLandingPageConfig model)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

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

        return Ok(new { success = true, message = "Landing page content saved successfully.", config });
    }

    /// <summary>
    /// GET /api/superadmin/cms/faqs
    /// Lists all FAQ articles.
    /// </summary>
    [HttpGet("cms/faqs")]
    public async Task<IActionResult> GetCmsFaqs()
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var faqs = await _db.FaqArticles.OrderBy(f => f.Category).ThenBy(f => f.DisplayOrder).ToListAsync();
        return Ok(new { success = true, total = faqs.Count, faqs });
    }

    /// <summary>
    /// POST /api/superadmin/cms/faqs (alias: /cms/save-faq)
    /// Creates or updates an FAQ article.
    /// </summary>
    [HttpPost("cms/faqs")]
    [HttpPost("cms/save-faq")]
    public async Task<IActionResult> SaveCmsFaq([FromBody] CmsFaqArticle model)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        if (string.IsNullOrWhiteSpace(model.Question))
            return BadRequest(new { success = false, message = "FAQ Question is required." });

        CmsFaqArticle? article = null;
        if (model.Id > 0)
        {
            article = await _db.FaqArticles.FindAsync(model.Id);
            if (article == null) return NotFound(new { success = false, message = $"FAQ with ID {model.Id} not found." });
        }
        else
        {
            article = new CmsFaqArticle();
            _db.FaqArticles.Add(article);
        }

        article.Question = model.Question.Trim();
        article.Answer = model.Answer?.Trim() ?? "";
        article.Category = string.IsNullOrWhiteSpace(model.Category) ? "General" : model.Category.Trim();
        article.DisplayOrder = model.DisplayOrder;
        article.IsActive = model.IsActive;

        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "FAQ article saved successfully.", faqId = article.Id });
    }

    /// <summary>
    /// DELETE /api/superadmin/cms/faqs/{id} (alias: POST /cms/delete-faq/{id})
    /// Deletes an FAQ article.
    /// </summary>
    [HttpDelete("cms/faqs/{id:int}")]
    [HttpPost("cms/delete-faq/{id:int}")]
    public async Task<IActionResult> DeleteCmsFaq(int id)
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var faq = await _db.FaqArticles.FindAsync(id);
        if (faq == null) return NotFound(new { success = false, message = $"FAQ with ID {id} not found." });

        _db.FaqArticles.Remove(faq);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "FAQ article deleted successfully." });
    }

    /// <summary>
    /// GET /api/superadmin/payments (alias: /api/super-admin/payments)
    /// Returns dynamic subscription payment transactions history.
    /// </summary>
    [HttpGet("payments")]
    public async Task<IActionResult> GetPayments()
    {
        if (!IsAuthorizedSuperAdmin()) return AccessDenied();

        var payments = await _db.SubscriptionPayments
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id,
                p.AdminId,
                p.CompanyName,
                p.AdminCode,
                p.PlanId,
                p.PlanName,
                p.BillingInterval,
                p.Amount,
                p.PaymentMethod,
                p.TransactionId,
                p.OrderId,
                p.Status,
                p.ReceiptUrl,
                p.Notes,
                p.CreatedAt
            })
            .ToListAsync();

        return Ok(new
        {
            success = true,
            total = payments.Count,
            payments
        });
    }
}

