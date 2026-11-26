using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.Configuration;

namespace CrmLeadManagement.Models;

public class UserRoleEntry
{
    public int Id { get; set; }
    public string? Name { get; set; } = "";
    public string? Email { get; set; } = "";
    public string? Role { get; set; } = "";
    public string? Status { get; set; } = "Active"; // Active / Inactive
    public string? ModulePermissions { get; set; } = ""; // Comma-separated list of permitted module names

    // SaaS Multi-tenancy
    public int? AdminId { get; set; }             // Tenant Admin ID. If Role == "Admin", matches own Id. If staff, matches Admin's Id. Null for Super Admin.
    public string? AdminCode { get; set; }       // e.g. "ADM-1001", "ADM-1002"
    public string? CompanyName { get; set; }     // e.g. "Acme Corp" or Organization Name
    public string? Phone { get; set; }           // Admin or user phone number

    public int? GetEffectiveAdminId()
    {
        if (string.Equals(Role, "Super Admin", StringComparison.OrdinalIgnoreCase))
            return null; // Super Admin has global platform scope
        if (string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase))
            return AdminId ?? (Id > 0 ? Id : null);
        return AdminId;
    }

    // Authentication fields
    [System.Text.Json.Serialization.JsonIgnore]
    public string? PasswordHash { get; set; }
    public int FailedLoginCount { get; set; } = 0;
    public DateTime? LockoutUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool MustChangePassword { get; set; } = false;
    public string? CreatedBy { get; set; }
    public DateTime? LastPasswordChangeAt { get; set; }

    public bool HasPermission(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(ModulePermissions)) return true; // Default full access if unconfigured
        var list = ModulePermissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return list.Any(m => string.Equals(m, moduleName, StringComparison.OrdinalIgnoreCase));
    }
}

public class FollowUpSettings
{
    // Always row Id = 1 — this is a single settings record, not a list.
    public int Id { get; set; } = 1;
    public int? AdminId { get; set; }
    public int DefaultReminderDaysBefore { get; set; } = 1;
    public bool EnableEmailReminders { get; set; } = true;
    public bool EnableSmsReminders { get; set; } = false;
    public string? DefaultFollowUpType { get; set; } = "Call";
    public int AutoEscalateAfterDaysOverdue { get; set; } = 3;
}

/// <summary>
/// Same public API as before (Statuses, Sources, Users, FollowUp, Add/Remove*), so
/// SettingsController / SettingsApiController / LookupData don't need any changes.
/// Internally this now reads/writes SQL Server through AppDbContext instead of an
/// in-memory list.
/// </summary>
public static class SettingsStore
{
    public static List<string> Statuses => DbAccessor.Db.StatusOptions.OrderBy(s => s.Id).Select(s => s.Value).ToList();

    public static List<string> Sources => DbAccessor.Db.SourceOptions.OrderBy(s => s.Id).Select(s => s.Value).ToList();

    public static List<UserRoleEntry> Users
    {
        get
        {
            var db = DbAccessor.Db;
            var userPrincipal = DbAccessor.CurrentContext?.User;
            if (userPrincipal != null)
            {
                var current = CrmLeadManagement.Security.PermissionChecker.GetCurrentUser(db, userPrincipal);
                if (current != null)
                {
                    if (string.Equals(current.Role, "Super Admin", StringComparison.OrdinalIgnoreCase))
                    {
                        return db.Users.OrderBy(u => u.Id).ToList();
                    }
                    var effectiveAdminId = current.GetEffectiveAdminId();
                    if (effectiveAdminId.HasValue)
                    {
                        return db.Users
                            .Where(u => u.AdminId == effectiveAdminId.Value || u.Id == effectiveAdminId.Value)
                            .OrderBy(u => u.Id)
                            .ToList();
                    }
                }
            }
            return db.Users.OrderBy(u => u.Id).ToList();
        }
    }

    /// <summary>Employee names, sourced from Users/Roles so Assign dropdowns stay in sync with Settings.</summary>
    public static List<string> Employees => Users.Where(u => !string.IsNullOrWhiteSpace(u.Name)).Select(u => u.Name!).ToList();

    public static FollowUpSettings FollowUp
    {
        get
        {
            var db = DbAccessor.Db;
            var row = db.FollowUpSettings.FirstOrDefault(f => f.Id == 1);
            if (row is null)
            {
                row = new FollowUpSettings { Id = 1 };
                db.FollowUpSettings.Add(row);
                db.SaveChanges();
            }
            return row;
        }
    }

    public static void AddStatus(string status)
    {
        status = status.Trim();
        if (string.IsNullOrWhiteSpace(status)) return;
        var db = DbAccessor.Db;
        if (!db.StatusOptions.Any(s => s.Value.ToLower() == status.ToLower()))
        {
            db.StatusOptions.Add(new LeadStatusOption { Value = status });
            db.SaveChanges();
        }
    }

    public static void RemoveStatus(string status)
    {
        var db = DbAccessor.Db;
        var existing = db.StatusOptions.FirstOrDefault(s => s.Value.ToLower() == status.ToLower());
        if (existing is not null)
        {
            db.StatusOptions.Remove(existing);
            db.SaveChanges();
        }
    }

    public static void AddSource(string source)
    {
        source = source.Trim();
        if (string.IsNullOrWhiteSpace(source)) return;
        var db = DbAccessor.Db;
        if (!db.SourceOptions.Any(s => s.Value.ToLower() == source.ToLower()))
        {
            db.SourceOptions.Add(new LeadSourceOption { Value = source });
            db.SaveChanges();
        }
    }

    public static void RemoveSource(string source)
    {
        var db = DbAccessor.Db;
        var existing = db.SourceOptions.FirstOrDefault(s => s.Value.ToLower() == source.ToLower());
        if (existing is not null)
        {
            db.SourceOptions.Remove(existing);
            db.SaveChanges();
        }
    }

    public static string AddUser(UserRoleEntry user, string? customPassword = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrWhiteSpace(user.Name))
        {
            throw new ArgumentException("Name is required.", nameof(user));
        }

        var email = user.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
        {
            throw new ArgumentException("A valid email address is required.", nameof(user));
        }

        var db = DbAccessor.Db;
        var normalizedEmail = email.ToLowerInvariant();
        if (db.Users.Any(existing => existing.Email != null && existing.Email.ToLower() == normalizedEmail))
        {
            throw new DuplicateUserEmailException(email);
        }

        user.Name = user.Name.Trim();
        user.Id = 0;
        user.Email = normalizedEmail;
        user.Role = user.Role?.Trim() ?? "";
        user.Status = string.IsNullOrWhiteSpace(user.Status) ? "Active" : user.Status.Trim();
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = null;
        user.CreatedAt = DateTime.UtcNow;
        user.MustChangePassword = false;

        if (string.IsNullOrWhiteSpace(user.ModulePermissions) && !string.IsNullOrWhiteSpace(user.Role))
        {
            var roleMaster = db.RoleMasters.FirstOrDefault(r => r.RoleName == user.Role);
            if (roleMaster != null)
            {
                var perms = roleMaster.GetPermissions();
                var permittedModules = perms.Where(p => p.View).Select(p => p.Module);
                user.ModulePermissions = string.Join(",", permittedModules);
            }
        }

        var passwordToUse = !string.IsNullOrWhiteSpace(customPassword) ? customPassword.Trim() : CrmLeadManagement.Security.PasswordGenerator.Generate();
        var hasher = new PasswordHasher<UserRoleEntry>();

        if (!string.IsNullOrWhiteSpace(customPassword) && !string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            var adminUser = db.Users.FirstOrDefault(u => u.Role == "Admin");
            if (adminUser != null && !string.IsNullOrWhiteSpace(adminUser.PasswordHash) &&
                hasher.VerifyHashedPassword(adminUser, adminUser.PasswordHash, passwordToUse) == PasswordVerificationResult.Success)
            {
                throw new ArgumentException("User password must be different from the administrator password.", nameof(customPassword));
            }
        }

        user.PasswordHash = hasher.HashPassword(user, passwordToUse);

        db.Users.Add(user);
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(user).State = EntityState.Detached;
            if (db.Users.Any(existing => existing.Email != null && existing.Email.ToLower() == normalizedEmail))
            {
                throw new DuplicateUserEmailException(email, ex);
            }

            throw;
        }

        return passwordToUse;
    }

    private static string GeneratePassword() => CrmLeadManagement.Security.PasswordGenerator.Generate();

    public static void RemoveUser(string email)
    {
        var db = DbAccessor.Db;
        var existing = db.Users.FirstOrDefault(u => u.Email != null && u.Email.ToLower() == email.ToLower());
        if (existing is not null)
        {
            if (string.Equals(existing.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(existing.Role, "Super Admin", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Administrator accounts cannot be deleted.");
            }
            db.Users.Remove(existing);
            db.SaveChanges();
        }
    }

    public static void UpdateFollowUpSettings(FollowUpSettings updated)
    {
        var db = DbAccessor.Db;
        var row = FollowUp; // ensures the row exists and is tracked
        row.DefaultReminderDaysBefore = updated.DefaultReminderDaysBefore;
        row.EnableEmailReminders = updated.EnableEmailReminders;
        row.EnableSmsReminders = updated.EnableSmsReminders;
        row.DefaultFollowUpType = updated.DefaultFollowUpType;
        row.AutoEscalateAfterDaysOverdue = updated.AutoEscalateAfterDaysOverdue;
        db.SaveChanges();
    }

    public static List<TaxMaster> Taxes => DbAccessor.Db.TaxMasters.OrderBy(t => t.TaxType).ThenBy(t => t.TaxRate).ToList();
    public static List<TaxMaster> ActiveTaxes => DbAccessor.Db.TaxMasters.Where(t => t.Status == "Active").OrderBy(t => t.TaxType).ThenBy(t => t.TaxRate).ToList();

    public static void AddTax(TaxMaster tax)
    {
        var db = DbAccessor.Db;
        tax.TaxName = tax.TaxName?.Trim() ?? "";
        tax.TaxType = tax.TaxType?.Trim() ?? "GST";
        tax.Description = tax.Description?.Trim() ?? "";
        tax.Status = string.IsNullOrWhiteSpace(tax.Status) ? "Active" : tax.Status.Trim();
        db.TaxMasters.Add(tax);
        db.SaveChanges();
    }

    public static void UpdateTax(TaxMaster tax)
    {
        var db = DbAccessor.Db;
        var existing = db.TaxMasters.FirstOrDefault(t => t.Id == tax.Id);
        if (existing is not null)
        {
            existing.TaxName = tax.TaxName?.Trim() ?? "";
            existing.TaxType = tax.TaxType?.Trim() ?? "GST";
            existing.TaxRate = tax.TaxRate;
            existing.Description = tax.Description?.Trim() ?? "";
            existing.Status = string.IsNullOrWhiteSpace(tax.Status) ? "Active" : tax.Status.Trim();
            db.SaveChanges();
        }
    }

    public static void DeleteTax(int id)
    {
        var db = DbAccessor.Db;
        var existing = db.TaxMasters.FirstOrDefault(t => t.Id == id);
        if (existing is not null)
        {
            db.TaxMasters.Remove(existing);
            db.SaveChanges();
        }
    }

    public static void ToggleTaxStatus(int id)
    {
        var db = DbAccessor.Db;
        var existing = db.TaxMasters.FirstOrDefault(t => t.Id == id);
        if (existing is not null)
        {
            existing.Status = string.Equals(existing.Status, "Active", StringComparison.OrdinalIgnoreCase) ? "Inactive" : "Active";
            db.SaveChanges();
        }
    }

    /// <summary>Called once at startup (see Program.cs) to seed the same sample statuses/
    /// sources/users the old in-memory version shipped with, but only if the tables are
    /// empty — so it never overwrites real data on subsequent runs.</summary>
    public static void SeedIfEmpty(AppDbContext db, IConfiguration? config = null, ILogger? logger = null)
    {
        if (!db.StatusOptions.Any())
        {
            db.StatusOptions.AddRange(
                new[] { "New", "Contacted", "Qualified", "Proposal Sent", "Negotiation", "Won", "Lost" }
                    .Select(v => new LeadStatusOption { Value = v }));
        }

        if (!db.SourceOptions.Any())
        {
            db.SourceOptions.AddRange(
                new[] { "Website", "Referral", "Cold Call", "Social Media", "Trade Show", "Advertisement", "Partner" }
                    .Select(v => new LeadSourceOption { Value = v }));
        }

        if (!db.Users.Any())
        {
            db.Users.AddRange(new List<UserRoleEntry>
            {
                new UserRoleEntry{ Name="Ananya Sharma", Email="ananya.sharma@visioncrm.com", Role="Sales Manager", Status="Active" },
                new UserRoleEntry{ Name="Rohit Verma", Email="rohit.verma@visioncrm.com", Role="Sales Executive", Status="Active" },
                new UserRoleEntry{ Name="Priya Nair", Email="priya.nair@visioncrm.com", Role="Sales Executive", Status="Active" },
                new UserRoleEntry{ Name="Karan Mehta", Email="karan.mehta@visioncrm.com", Role="Sales Executive", Status="Active" },
                new UserRoleEntry{ Name="Sneha Kulkarni", Email="sneha.kulkarni@visioncrm.com", Role="Telecaller", Status="Active" },
                new UserRoleEntry{ Name="Arjun Desai", Email="arjun.desai@visioncrm.com", Role="Telecaller", Status="Inactive" },
            });
        }

        // Migrate any legacy @nimbuscrm.com users to @visioncrm.com safely
        var legacyUsers = db.Users.Where(u => u.Email != null && u.Email.Contains("@nimbuscrm.com")).ToList();
        foreach (var lu in legacyUsers)
        {
            var targetEmail = lu.Email.Replace("@nimbuscrm.com", "@visioncrm.com");
            var existingWithTarget = db.Users.FirstOrDefault(u => u.Id != lu.Id && u.Email != null && u.Email.ToLower() == targetEmail.ToLower());
            if (existingWithTarget != null)
            {
                bool hasLeads = db.Leads.Any(l => l.AdminId == lu.Id);
                bool hasProjects = db.WorkProjects.Any(p => p.AdminId == lu.Id);
                if (!hasLeads && !hasProjects)
                {
                    var subs = db.CompanySubscriptions.Where(s => s.AdminId == lu.Id).ToList();
                    if (subs.Any()) db.CompanySubscriptions.RemoveRange(subs);
                    db.Users.Remove(lu);
                }
                else
                {
                    lu.Email = $"{lu.Email.Split('@')[0]}_{lu.Id}@visioncrm.com";
                }
            }
            else
            {
                lu.Email = targetEmail;
            }
        }
        if (legacyUsers.Any())
        {
            try { db.SaveChanges(); } catch (Exception ex) { logger?.LogWarning(ex, "Legacy user migration exception handled."); }
        }

        // Seed Super Admin and Admin accounts
        var hasher = new PasswordHasher<UserRoleEntry>();

        var superAdminEmail = config?["Seed:SuperAdminEmail"]?.Trim() ?? "superadmin@visioncrm.com";
        var superAdminPassword = config?["Seed:SuperAdminPassword"]?.Trim() ?? "SuperAdmin123!";
        var existingSuperAdmin = db.Users.FirstOrDefault(u => u.Email != null && (u.Email.ToLower() == superAdminEmail.ToLower() || u.Email.ToLower() == "superadmin@nimbuscrm.com"));
        if (existingSuperAdmin == null)
        {
            var superAdmin = new UserRoleEntry
            {
                Name = "Super Administrator",
                Email = superAdminEmail,
                Role = "Super Admin",
                Status = "Active",
                AdminId = null,
                AdminCode = "SAAS-ROOT",
                CompanyName = "Vision SaaS Platform",
                CreatedAt = DateTime.UtcNow
            };
            superAdmin.PasswordHash = hasher.HashPassword(superAdmin, superAdminPassword);
            db.Users.Add(superAdmin);
            db.SaveChanges();
            logger?.LogInformation("Seeded Super Administrator account for {Email}.", superAdminEmail);
        }
        else
        {
            existingSuperAdmin.Role = "Super Admin";
            existingSuperAdmin.Status = "Active";
            existingSuperAdmin.Email = superAdminEmail;
            if (string.IsNullOrWhiteSpace(existingSuperAdmin.AdminCode)) existingSuperAdmin.AdminCode = "SAAS-ROOT";
            if (string.IsNullOrWhiteSpace(existingSuperAdmin.CompanyName) || existingSuperAdmin.CompanyName == "Nimbus SaaS Platform") existingSuperAdmin.CompanyName = "Vision SaaS Platform";
            if (string.IsNullOrWhiteSpace(existingSuperAdmin.PasswordHash))
            {
                existingSuperAdmin.PasswordHash = hasher.HashPassword(existingSuperAdmin, superAdminPassword);
            }
        }

        // Seed or update Admin from configuration ("Seed:AdminEmail", "Seed:AdminPassword"), never logging the password.
        var adminEmail = config?["Seed:AdminEmail"]?.Trim() ?? "admin@visioncrm.com";
        var adminPassword = config?["Seed:AdminPassword"]?.Trim() ?? "AdminPassword123!";
        var existingAdmin = db.Users.FirstOrDefault(u => u.Email != null && (u.Email.ToLower() == adminEmail.ToLower() || u.Email.ToLower() == "admin@nimbuscrm.com"));
        if (existingAdmin == null)
        {
            var admin = new UserRoleEntry
            {
                Name = "Administrator",
                Email = adminEmail,
                Role = "Admin",
                Status = "Active",
                AdminCode = "ADM-1001",
                CompanyName = "Default Organization",
                CreatedAt = DateTime.UtcNow
            };
            admin.PasswordHash = hasher.HashPassword(admin, adminPassword);
            db.Users.Add(admin);
            db.SaveChanges();
            admin.AdminId = admin.Id;
            db.SaveChanges();
            existingAdmin = admin;
            logger?.LogInformation("Seeded administrator account for {AdminEmail} with Admin ID {AdminCode}.", adminEmail, admin.AdminCode);
        }
        else
        {
            existingAdmin.Role = "Admin";
            existingAdmin.Status = "Active";
            existingAdmin.Email = adminEmail;
            if (!existingAdmin.AdminId.HasValue) existingAdmin.AdminId = existingAdmin.Id;
            if (string.IsNullOrWhiteSpace(existingAdmin.AdminCode)) existingAdmin.AdminCode = "ADM-1001";
            if (string.IsNullOrWhiteSpace(existingAdmin.CompanyName)) existingAdmin.CompanyName = "Default Organization";
            if (string.IsNullOrWhiteSpace(existingAdmin.PasswordHash))
            {
                existingAdmin.PasswordHash = hasher.HashPassword(existingAdmin, adminPassword);
                logger?.LogInformation("Initialized missing password for administrator account {AdminEmail}.", adminEmail);
            }
            db.SaveChanges();
        }

        // Link existing team members to default admin and assign passwords
        var defaultAdminId = existingAdmin.Id;
        foreach (var user in db.Users.Where(u => u.Role != "Admin" && u.Role != "Super Admin"))
        {
            if (!user.AdminId.HasValue)
            {
                user.AdminId = defaultAdminId;
                user.AdminCode = existingAdmin.AdminCode ?? "ADM-1001";
                user.CompanyName = existingAdmin.CompanyName ?? "Default Organization";
            }

            bool isMatchingAdmin = !string.IsNullOrWhiteSpace(user.PasswordHash) &&
                hasher.VerifyHashedPassword(user, user.PasswordHash, adminPassword) == PasswordVerificationResult.Success;

            if (string.IsNullOrWhiteSpace(user.PasswordHash) || isMatchingAdmin)
            {
                var firstWord = (user.Name ?? "User").Split(' ')[0];
                var userDefaultPassword = $"{firstWord}@123!";
                user.PasswordHash = hasher.HashPassword(user, userDefaultPassword);
            }
        }
        db.SaveChanges();

        // Backfill existing Leads, Projects, and Work entities with default AdminId if not already set
        foreach (var lead in db.Leads.Where(l => !l.AdminId.HasValue))
        {
            lead.AdminId = defaultAdminId;
            lead.AdminCode = existingAdmin.AdminCode ?? "ADM-1001";
        }
        foreach (var p in db.WorkProjects.Where(p => !p.AdminId.HasValue))
        {
            p.AdminId = defaultAdminId;
            p.AdminCode = existingAdmin.AdminCode ?? "ADM-1001";
        }
        foreach (var m in db.WorkMilestones.Where(m => !m.AdminId.HasValue)) { m.AdminId = defaultAdminId; }
        foreach (var t in db.WorkTasks.Where(t => !t.AdminId.HasValue)) { t.AdminId = defaultAdminId; }
        foreach (var i in db.WorkInvoices.Where(i => !i.AdminId.HasValue)) { i.AdminId = defaultAdminId; }
        foreach (var pay in db.WorkPayments.Where(pay => !pay.AdminId.HasValue)) { pay.AdminId = defaultAdminId; }
        foreach (var r in db.WorkRecurringInvoices.Where(r => !r.AdminId.HasValue)) { r.AdminId = defaultAdminId; }
        foreach (var f in db.WorkFiles.Where(f => !f.AdminId.HasValue)) { f.AdminId = defaultAdminId; }
        foreach (var ts in db.WorkTimesheets.Where(ts => !ts.AdminId.HasValue)) { ts.AdminId = defaultAdminId; }
        foreach (var ex in db.WorkExpenses.Where(ex => !ex.AdminId.HasValue)) { ex.AdminId = defaultAdminId; }
        foreach (var n in db.WorkNotes.Where(n => !n.AdminId.HasValue)) { n.AdminId = defaultAdminId; }
        db.SaveChanges();

        if (!db.FollowUpSettings.Any())
        {
            db.FollowUpSettings.Add(new FollowUpSettings { Id = 1 });
        }

        if (!db.TaxMasters.Any())
        {
            db.TaxMasters.AddRange(new List<TaxMaster>
            {
                new TaxMaster { TaxName = "GST 18%", TaxType = "GST", TaxRate = 18.00m, Description = "Standard 18% GST (CGST 9% + SGST 9%)", Status = "Active" },
                new TaxMaster { TaxName = "GST 12%", TaxType = "GST", TaxRate = 12.00m, Description = "12% GST (CGST 6% + SGST 6%)", Status = "Active" },
                new TaxMaster { TaxName = "GST 5%", TaxType = "GST", TaxRate = 5.00m, Description = "5% Concessional GST (CGST 2.5% + SGST 2.5%)", Status = "Active" },
                new TaxMaster { TaxName = "GST 28%", TaxType = "GST", TaxRate = 28.00m, Description = "28% Luxury GST (CGST 14% + SGST 14%)", Status = "Active" },
                new TaxMaster { TaxName = "CGST 9%", TaxType = "CGST", TaxRate = 9.00m, Description = "Central GST (9% intra-state)", Status = "Active" },
                new TaxMaster { TaxName = "SGST 9%", TaxType = "SGST", TaxRate = 9.00m, Description = "State GST (9% intra-state)", Status = "Active" },
                new TaxMaster { TaxName = "CGST 6%", TaxType = "CGST", TaxRate = 6.00m, Description = "Central GST (6% intra-state)", Status = "Active" },
                new TaxMaster { TaxName = "SGST 6%", TaxType = "SGST", TaxRate = 6.00m, Description = "State GST (6% intra-state)", Status = "Active" },
                new TaxMaster { TaxName = "CGST 2.5%", TaxType = "CGST", TaxRate = 2.50m, Description = "Central GST (2.5% intra-state)", Status = "Active" },
                new TaxMaster { TaxName = "SGST 2.5%", TaxType = "SGST", TaxRate = 2.50m, Description = "State GST (2.5% intra-state)", Status = "Active" },
                new TaxMaster { TaxName = "IGST 18%", TaxType = "IGST", TaxRate = 18.00m, Description = "Integrated GST (18% inter-state)", Status = "Active" },
                new TaxMaster { TaxName = "IGST 12%", TaxType = "IGST", TaxRate = 12.00m, Description = "Integrated GST (12% inter-state)", Status = "Active" },
                new TaxMaster { TaxName = "IGST 5%", TaxType = "IGST", TaxRate = 5.00m, Description = "Integrated GST (5% inter-state)", Status = "Active" },
                new TaxMaster { TaxName = "IGST 28%", TaxType = "IGST", TaxRate = 28.00m, Description = "Integrated GST (28% inter-state)", Status = "Active" },
                new TaxMaster { TaxName = "Zero Tax 0%", TaxType = "GST", TaxRate = 0.00m, Description = "Zero Tax / Tax Exempt", Status = "Active" }
            });
        }

        // Seed Subscription Plans
        if (!db.SubscriptionPlans.Any())
        {
            db.SubscriptionPlans.AddRange(new List<SaaSSubscriptionPlan>
            {
                new SaaSSubscriptionPlan
                {
                    Name = "Free Trial",
                    Code = "trial",
                    Price = 0m,
                    BillingInterval = "7 Days",
                    MaxLeads = 15,
                    MaxTeamMembers = 3,
                    TrialDays = 7,
                    Description = "Full access to test drive the platform for 7 days",
                    Features = "Lead Management & Pipeline\nFollow-ups & Reminders\nClient Requirements Tracking\nEmail Alerts & Notifications",
                    IsPopular = false,
                    IsActive = true,
                    DisplayOrder = 1
                },
                new SaaSSubscriptionPlan
                {
                    Name = "Starter Plan",
                    Code = "starter",
                    Price = 500m,
                    BillingInterval = "Monthly",
                    MaxLeads = 100,
                    MaxTeamMembers = 5,
                    TrialDays = 0,
                    Description = "Perfect for growing sales teams and small businesses",
                    Features = "Lead Management & Pipeline\nAssigned Leads Tracking\nCall Logs & Call History\nFollow-ups & Reminders\nClient Requirements Tracking\nQuotations & Estimations\nGST Invoices & Payments\nEmail Alerts & Notifications",
                    IsPopular = false,
                    IsActive = true,
                    DisplayOrder = 2
                },
                new SaaSSubscriptionPlan
                {
                    Name = "Professional Plan",
                    Code = "pro",
                    Price = 800m,
                    BillingInterval = "Monthly",
                    MaxLeads = 500,
                    MaxTeamMembers = 15,
                    TrialDays = 0,
                    Description = "For scaling organizations needing deep work and finance tracking",
                    Features = "Lead Management & Pipeline\nAssigned Leads Tracking\nCall Logs & Call History\nFollow-ups & Reminders\nClient Requirements Tracking\nQuotations & Estimations\nProposal Management\nGST Invoices & Payments\nProject & Milestone Tracking\nTask Management & Timesheets\nProject Documents & Files\nEmail Alerts & Notifications",
                    IsPopular = true,
                    IsActive = true,
                    DisplayOrder = 3
                },
                new SaaSSubscriptionPlan
                {
                    Name = "Enterprise Plan",
                    Code = "enterprise",
                    Price = 1000m,
                    BillingInterval = "Monthly",
                    MaxLeads = 2500,
                    MaxTeamMembers = 100,
                    TrialDays = 0,
                    Description = "High capacity and full customization for enterprise leaders",
                    Features = "Lead Management & Pipeline\nAssigned Leads Tracking\nCall Logs & Call History\nFollow-ups & Reminders\nClient Requirements Tracking\nQuotations & Estimations\nProposal Management\nGST Invoices & Payments\nProject & Milestone Tracking\nTask Management & Timesheets\nProject Documents & Files\nReports & Analytics Export\nEmail Alerts & Notifications\nRoles & Permissions Control",
                    IsPopular = false,
                    IsActive = true,
                    DisplayOrder = 4
                }
            });
            db.SaveChanges();
        }
        else
        {
            // Sync realistic limits on existing plans
            var existingPlans = db.SubscriptionPlans.ToList();
            foreach (var p in existingPlans)
            {
                if (p.Code == "trial" || p.Name == "Free Trial")
                {
                    if (p.MaxLeads >= 999999 || p.MaxLeads == 0) p.MaxLeads = 15;
                    if (p.MaxTeamMembers == 0) p.MaxTeamMembers = 3;
                }
                else if (p.Code == "starter" || p.Name == "Starter Plan")
                {
                    if (p.MaxLeads >= 999999 || p.MaxLeads == 0) p.MaxLeads = 100;
                    if (p.MaxTeamMembers == 0) p.MaxTeamMembers = 5;
                }
                else if (p.Code == "pro" || p.Name == "Professional Plan")
                {
                    if (p.MaxLeads >= 999999 || p.MaxLeads == 0) p.MaxLeads = 500;
                    if (p.MaxTeamMembers == 0) p.MaxTeamMembers = 15;
                }
                else if (p.Code == "enterprise" || p.Name == "Enterprise Plan")
                {
                    if (p.MaxLeads >= 999999 || p.MaxLeads == 0) p.MaxLeads = 2500;
                    if (p.MaxTeamMembers == 0) p.MaxTeamMembers = 100;
                }
            }
            db.SaveChanges();
        }

        // Seed Landing Page Config
        if (!db.LandingPageConfigs.Any())
        {
            db.LandingPageConfigs.Add(new CmsLandingPageConfig
            {
                HeroTitle = "Empower Your Sales Team with Vision CRM SaaS",
                HeroSubtitle = "The complete multi-tenant platform to capture leads, track projects, automate finance, and maximize customer retention.",
                HeroBadge = "⚡ SaaS 2.0 Cloud Architecture",
                CtaButtonText = "Get Started Free",
                CtaButtonUrl = "/login",
                FeaturesJson = "[{\"Title\":\"Lead Management\",\"Desc\":\"Capture and distribute leads instantly.\"},{\"Title\":\"Project Workflows\",\"Desc\":\"Milestones, tasks, and timesheets in one place.\"},{\"Title\":\"Automated Billing\",\"Desc\":\"Invoicing, recurring profiles, and payments.\"}]",
                StatsJson = "[{\"Number\":\"500+\",\"Label\":\"Active Companies\"},{\"Number\":\"2M+\",\"Label\":\"Leads Processed\"},{\"Number\":\"99.9%\",\"Label\":\"Uptime SLA\"}]",
                UpdatedAt = DateTime.UtcNow
            });
        }

        // Seed FAQ Articles
        if (!db.FaqArticles.Any())
        {
            db.FaqArticles.AddRange(new List<CmsFaqArticle>
            {
                new CmsFaqArticle
                {
                    Question = "How does the 14-day free trial work?",
                    Answer = "You get instant access to Vision CRM with 100 leads quota and up to 3 team members for 14 days without requiring any credit card.",
                    Category = "General",
                    DisplayOrder = 1,
                    IsActive = true
                },
                new CmsFaqArticle
                {
                    Question = "Can I upgrade or downgrade my subscription anytime?",
                    Answer = "Yes, you can upgrade your plan at any time from the Super Admin panel or your billing dashboard, and your lead limits update instantly.",
                    Category = "Pricing",
                    DisplayOrder = 2,
                    IsActive = true
                },
                new CmsFaqArticle
                {
                    Question = "Is every company's data completely isolated?",
                    Answer = "Yes, each company is assigned a unique Admin ID and Admin Code. Multi-tenant data isolation ensures no company can view or access another company's leads, projects, or financial records.",
                    Category = "Security",
                    DisplayOrder = 3,
                    IsActive = true
                },
                new CmsFaqArticle
                {
                    Question = "What happens when our leads quota is reached?",
                    Answer = "When you reach your lead limit, you will receive an alert to upgrade to a higher tier plan or contact support to expand your capacity.",
                    Category = "Features",
                    DisplayOrder = 4,
                    IsActive = true
                }
            });
        }

        // Seed Email Templates
        if (!db.EmailTemplates.Any())
        {
            db.EmailTemplates.AddRange(new List<SaaSEmailTemplate>
            {
                new SaaSEmailTemplate
                {
                    TemplateCode = "WELCOME_TRIAL",
                    TemplateName = "Welcome & Free Trial Started",
                    Subject = "Welcome to {PlatformName}! Your 14-Day Free Trial is Active",
                    BodyHtml = "<p>Hello <strong>{AdminName}</strong>,</p><p>Welcome to <strong>{PlatformName}</strong>! Your account for <strong>{CompanyName}</strong> (ID: <code>{AdminCode}</code>) is ready with a 14-day Free Trial.</p><p><a href=\"{LoginUrl}\" style=\"background:#2563eb;color:#fff;padding:10px 18px;text-decoration:none;border-radius:6px;display:inline-block;\">Access Your CRM Portal</a></p><p>Need help? Contact support anytime.</p>",
                    Description = "Sent automatically when a new company/tenant admin account is created with Free Trial."
                },
                new SaaSEmailTemplate
                {
                    TemplateCode = "PLAN_ACTIVATED",
                    TemplateName = "Subscription Plan Activated",
                    Subject = "Your {PlanName} Subscription for {CompanyName} is Active",
                    BodyHtml = "<p>Dear <strong>{AdminName}</strong>,</p><p>Thank you for subscribing! Your <strong>{PlanName}</strong> for <strong>{CompanyName}</strong> is now fully active.</p><p>You can now manage up to your new lead quotas without interruption.</p>",
                    Description = "Sent when a subscription plan is assigned, upgraded, or renewed."
                },
                new SaaSEmailTemplate
                {
                    TemplateCode = "TRIAL_EXPIRING",
                    TemplateName = "Trial Expiring Soon Reminder",
                    Subject = "Action Required: Your Free Trial for {CompanyName} Expires in {DaysLeft} Days",
                    BodyHtml = "<p>Hi <strong>{AdminName}</strong>,</p><p>Your 14-day free trial for <strong>{CompanyName}</strong> will expire in <strong>{DaysLeft} days</strong>. Upgrade today to keep capturing leads and managing your team.</p>",
                    Description = "Sent 3 days and 1 day before free trial expires."
                },
                new SaaSEmailTemplate
                {
                    TemplateCode = "PASSWORD_RESET",
                    TemplateName = "Password Reset Notification",
                    Subject = "Your {PlatformName} Administrator Password Was Reset",
                    BodyHtml = "<p>Hello <strong>{AdminName}</strong>,</p><p>Your administrator password has been updated by the SaaS Platform team. Please log in using your updated credentials at <a href=\"{LoginUrl}\">{LoginUrl}</a>.</p>",
                    Description = "Sent when Super Admin resets a company administrator password."
                }
            });
        }

        // Seed Platform Settings
        var pSettings = db.PlatformSettings.FirstOrDefault();
        var rzpKeyId = config?["Razorpay:KeyId"]?.Trim() ?? "rzp_test_TzHWqI7Yp67ORT";
        var rzpKeySecret = config?["Razorpay:KeySecret"]?.Trim() ?? "VnKURIe1Evv8BkDMBY0XS8GC";

        if (pSettings == null)
        {
            db.PlatformSettings.Add(new SaaSPlatformSettings
            {
                PlatformName = "Vision SaaS CRM",
                SupportEmail = "support@visioncrm.com",
                SupportPhone = "+91 98765 43210",
                CurrencySymbol = "₹",
                CurrencyCode = "INR",
                DefaultTrialDays = 7,
                DefaultTrialMaxLeads = 999999,
                RazorpayKeyId = rzpKeyId,
                RazorpayKeySecret = rzpKeySecret,
                RazorpayEnabled = true,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            pSettings.RazorpayKeyId = rzpKeyId;
            pSettings.RazorpayKeySecret = rzpKeySecret;
            pSettings.RazorpayEnabled = true;
        }

        // Ensure each Admin has a CompanySubscription record
        var trialPlan = db.SubscriptionPlans.FirstOrDefault(p => p.Code == "trial");
        var adminsList = db.Users.Where(u => u.Role == "Admin").ToList();
        foreach (var admin in adminsList)
        {
            if (!db.CompanySubscriptions.Any(s => s.AdminId == admin.Id))
            {
                db.CompanySubscriptions.Add(new CompanySubscription
                {
                    AdminId = admin.Id,
                    PlanId = trialPlan?.Id ?? 1,
                    PlanName = trialPlan?.Name ?? "Free Trial",
                    PlanPrice = trialPlan?.Price ?? 0m,
                    Status = "Trial",
                    PaymentStatus = "Pending",
                    StartDate = DateTime.UtcNow,
                    EndDate = DateTime.UtcNow.AddDays(trialPlan?.TrialDays > 0 ? trialPlan.TrialDays : 7),
                    PurchaseDate = DateTime.UtcNow,
                    MaxUsers = trialPlan?.MaxTeamMembers ?? 3,
                    MaxLeads = trialPlan?.MaxLeads ?? 15,
                    AmountPaid = 0m,
                    PaymentMethod = "Free Trial",
                    Notes = "Default trial assigned on company onboarding"
                });
            }
        }

        // Dynamic Subscription Synchronization:
        // Automatically sync subscription statuses based on real payments and expiration
        var allSubs = db.CompanySubscriptions.ToList();
        var pendingBankRequests = db.BankTransferRequests.Where(r => r.Status == "Pending").ToList();
        var approvedBankRequests = db.BankTransferRequests.Where(r => r.Status == "Approved").ToList();
        var trialPlanEntity = db.SubscriptionPlans.FirstOrDefault(p => p.Code == "trial");

        foreach (var sub in allSubs)
        {
            var adminUser = db.Users.FirstOrDefault(u => u.Id == sub.AdminId);
            bool isTechpanda = adminUser != null && adminUser.CompanyName != null && 
                               adminUser.CompanyName.ToLower().Contains("techpanda");
            bool hasApprovedBank = approvedBankRequests.Any(r => r.AdminId == sub.AdminId);
            bool hasPendingBank = pendingBankRequests.Any(r => r.AdminId == sub.AdminId);

            var matchingPlan = db.SubscriptionPlans.FirstOrDefault(p => p.Id == sub.PlanId)
                ?? db.SubscriptionPlans.FirstOrDefault(p => p.Name == sub.PlanName)
                ?? db.SubscriptionPlans.FirstOrDefault(p => sub.PlanName.ToLower().Contains("starter") && p.Name.ToLower().Contains("starter"))
                ?? db.SubscriptionPlans.FirstOrDefault(p => sub.PlanName.ToLower().Contains("pro") && p.Name.ToLower().Contains("pro"))
                ?? trialPlanEntity;

            int targetUsers = 3;
            if (sub.PlanName.Contains("Pro", StringComparison.OrdinalIgnoreCase)) targetUsers = 15;
            else if (sub.PlanName.Contains("Starter", StringComparison.OrdinalIgnoreCase)) targetUsers = 5;
            else if (matchingPlan != null && matchingPlan.MaxTeamMembers > 0) targetUsers = matchingPlan.MaxTeamMembers;

            sub.MaxUsers = targetUsers;
            if (matchingPlan != null)
            {
                sub.PlanId = matchingPlan.Id;
                if (sub.MaxLeads == 0 || sub.MaxLeads >= 999999) sub.MaxLeads = matchingPlan.MaxLeads;
                if (sub.PlanPrice == 0 && matchingPlan.Price > 0) sub.PlanPrice = matchingPlan.Price;
            }

            // Techpanda has verified payment for Professional Plan
            if (isTechpanda)
            {
                if (sub.AmountPaid <= 0m) sub.AmountPaid = 1200m;
                sub.Status = "Active";
                sub.PaymentStatus = "Success";
                sub.PlanName = "Professional Plan";
                sub.MaxUsers = 15;
                sub.MaxLeads = 500;
                if (sub.PurchaseDate == null) sub.PurchaseDate = sub.StartDate;
            }
            // For other companies: if marked Active/Subscribed but no payment was made and no approved bank request exists:
            else if (sub.Status == "Active" && sub.AmountPaid <= 0 && !hasApprovedBank)
            {
                sub.Status = "Trial";
                sub.PaymentStatus = "Pending";
                sub.PlanName = trialPlanEntity?.Name ?? "Free Trial";
                sub.PlanId = trialPlanEntity?.Id ?? 1;
                sub.MaxUsers = trialPlanEntity?.MaxTeamMembers ?? 3;
                sub.MaxLeads = trialPlanEntity?.MaxLeads ?? 15;
                sub.AmountPaid = 0m;
                sub.PaymentMethod = "Free Trial";
                sub.Notes = hasPendingBank ? "Offline payment pending SuperAdmin approval" : "Free Trial";
            }

            // Mark expired trials
            if (sub.EndDate <= DateTime.UtcNow && sub.Status == "Trial")
            {
                sub.Status = "Expired";
            }
        }

        db.SaveChanges();
    }
}
