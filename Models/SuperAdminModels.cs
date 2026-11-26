using System.ComponentModel.DataAnnotations;

namespace CrmLeadManagement.Models;

/// <summary>
/// SaaS Subscription Plan configured by SuperAdmin (Free Trial, Starter, Pro, Enterprise).
/// </summary>
public class SaaSSubscriptionPlan
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = "";

    public string Code { get; set; } = "";
    public decimal Price { get; set; } = 0m;
    public string BillingInterval { get; set; } = "Monthly"; // Monthly | Yearly
    public int MaxLeads { get; set; } = 10; // 10 for Trial, 100 for Starter, 500 for Pro, 2500 for Enterprise
    public int MaxTeamMembers { get; set; } = 3; // Max users
    public int TrialDays { get; set; } = 7;
    public string? Description { get; set; } = "";
    public string Features { get; set; } = ""; // Line-separated features list
    public bool IsPopular { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Active or Trial Subscription assigned to a Tenant Company (Admin).
/// </summary>
public class CompanySubscription
{
    public int Id { get; set; }

    public int AdminId { get; set; } // Matches the Admin's Id in Users table
    public int PlanId { get; set; }
    public string PlanName { get; set; } = "Free Trial";
    public decimal PlanPrice { get; set; } = 0m;
    public string Status { get; set; } = "Trial"; // Trial | Active | Expired | Suspended | Cancelled | Pending
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public DateTime EndDate { get; set; } = DateTime.UtcNow.AddDays(7);
    public DateTime? PurchaseDate { get; set; }
    public int MaxUsers { get; set; } = 3;
    public int MaxLeads { get; set; } = 10;
    public decimal AmountPaid { get; set; } = 0m;
    public string PaymentMethod { get; set; } = "Free Trial";
    public string PaymentStatus { get; set; } = "Pending"; // Pending | Success | Failed | Cancelled | Refunded
    public string? Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsExpired => DateTime.UtcNow > EndDate || string.Equals(Status, "Expired", StringComparison.OrdinalIgnoreCase);
    public int DaysRemaining => Math.Max(0, (int)Math.Ceiling((EndDate - DateTime.UtcNow).TotalDays));
}

/// <summary>
/// CMS: Public Landing Page Content Configuration.
/// </summary>
public class CmsLandingPageConfig
{
    public int Id { get; set; }
    public string HeroTitle { get; set; } = "Accelerate Your Business with Vision CRM SaaS";
    public string HeroSubtitle { get; set; } = "The ultimate multi-tenant platform to capture leads, track projects, manage finance, and empower your sales team.";
    public string HeroBadge { get; set; } = "✨ SaaS 2.0 Cloud Platform";
    public string CtaButtonText { get; set; } = "Start 7-Day Free Trial";
    public string CtaButtonUrl { get; set; } = "/login";
    public string FeaturesJson { get; set; } = "[]";
    public string StatsJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// CMS: FAQ Articles.
/// </summary>
public class CmsFaqArticle
{
    public int Id { get; set; }
    [Required]
    public string Question { get; set; } = "";
    [Required]
    public string Answer { get; set; } = "";
    public string Category { get; set; } = "General"; // General | Pricing | Features | Security
    public int DisplayOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Transactional and Notification Email Templates managed by SuperAdmin.
/// </summary>
public class SaaSEmailTemplate
{
    public int Id { get; set; }
    [Required]
    public string TemplateCode { get; set; } = ""; // WELCOME_TRIAL | PLAN_ACTIVATED | TRIAL_EXPIRING | INVOICE_RECEIPT | PASSWORD_RESET
    [Required]
    public string TemplateName { get; set; } = "";
    [Required]
    public string Subject { get; set; } = "";
    public string BodyHtml { get; set; } = "";
    public string Description { get; set; } = "";
    public string AvailableVariables { get; set; } = "{CompanyName}, {AdminName}, {AdminCode}, {PlanName}, {LoginUrl}, {DaysLeft}";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Global Platform Settings for SuperAdmin.
/// </summary>
public class SaaSPlatformSettings
{
    public int Id { get; set; }
    public string PlatformName { get; set; } = "Vision SaaS CRM";
    public string SupportEmail { get; set; } = "support@visioncrm.com";
    public string SupportPhone { get; set; } = "+91 98765 43210";
    public string CurrencySymbol { get; set; } = "₹";
    public string CurrencyCode { get; set; } = "INR";
    public int DefaultTrialDays { get; set; } = 7;
    public int DefaultTrialMaxLeads { get; set; } = 3;
    public string? RazorpayKeyId { get; set; } = "";
    public string? RazorpayKeySecret { get; set; } = "";
    public bool RazorpayEnabled { get; set; } = true;

    // Bank Transfer Offline Payment Configuration
    public bool BankTransferEnabled { get; set; } = true;
    public string? BankName { get; set; } = "HDFC Bank Ltd";
    public string? BankAccountName { get; set; } = "Vision SaaS Technologies Pvt Ltd";
    public string? BankAccountNumber { get; set; } = "50200012345678";
    public string? BankIfscCode { get; set; } = "HDFC0001234";
    public string? BankBranch { get; set; } = "Main Branch";
    public string? BankUpiId { get; set; } = "visioncrm@upi";
    public string? BankInstructions { get; set; } = "Please transfer on bank account";

    // SMTP Email Delivery Configuration
    public string? SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUser { get; set; } = "";
    public string? SmtpPassword { get; set; } = "";
    public string? SmtpFromEmail { get; set; } = "notifications@visioncrm.com";
    public string? SmtpFromName { get; set; } = "VisionCRM SaaS";
    public bool SmtpEnableSsl { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// View model for Tenant Admin Panel Subscription & Plans page.
/// </summary>
public class AdminSubscriptionViewModel
{
    public List<SaaSSubscriptionPlan> Plans { get; set; } = new();
    public CompanySubscription CurrentSubscription { get; set; } = new();
    public SaaSSubscriptionPlan? CurrentPlan { get; set; }
    public int CurrentUsersCount { get; set; }
    public int MaxUsers { get; set; } = 3;
    public int CurrentLeadsCount { get; set; }
    public int MaxLeads { get; set; } = 10;
    public int CurrentProjectsCount { get; set; }
    public SaaSPlatformSettings PlatformSettings { get; set; } = new();
    public string CompanyName { get; set; } = "";
    public string AdminCode { get; set; } = "";
    public bool IsAdmin { get; set; } = true;
    public BankTransferRequest? PendingBankRequest { get; set; }
    public List<SubscriptionPaymentRecord> RecentPayments { get; set; } = new();
}

/// <summary>
/// View model for Subscribe / Plan Checkout page (matching pricing summary & bank transfer).
/// </summary>
public class PlanCheckoutViewModel
{
    public SaaSSubscriptionPlan Plan { get; set; } = new();
    public CompanySubscription CurrentSubscription { get; set; } = new();
    public int CurrentUsersCount { get; set; }
    public string CompanyName { get; set; } = "";
    public string AdminCode { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public SaaSPlatformSettings PlatformSettings { get; set; } = new();
    public List<string> Features { get; set; } = new();
}

/// <summary>
/// Bank Transfer Subscription Request submitted by a Company for SuperAdmin review and approval.
/// </summary>
public class BankTransferRequest
{
    public int Id { get; set; }
    public int AdminId { get; set; }
    public string CompanyName { get; set; } = "";
    public string AdminCode { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public int PlanId { get; set; }
    public string PlanName { get; set; } = "";
    public decimal Amount { get; set; } = 0m;
    public string BillingInterval { get; set; } = "Monthly";
    public string? ReceiptFilePath { get; set; } = "";
    public string? TransactionReference { get; set; } = "";
    public string? UserNotes { get; set; } = "";
    public string Status { get; set; } = "Pending"; // Pending | Approved | Rejected
    public string? AdminRemarks { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}

/// <summary>
/// Individual subscription payment transaction history (Razorpay, Bank Transfer, Trial).
/// </summary>
public class SubscriptionPaymentRecord
{
    public int Id { get; set; }
    public int AdminId { get; set; }
    public string CompanyName { get; set; } = "";
    public string AdminCode { get; set; } = "";
    public int PlanId { get; set; }
    public string PlanName { get; set; } = "";
    public string BillingInterval { get; set; } = "Monthly";
    public decimal Amount { get; set; } = 0m;
    public string PaymentMethod { get; set; } = "Razorpay"; // Razorpay | Bank Transfer | Free Trial
    public string? TransactionId { get; set; } = ""; // Razorpay PaymentId or UTR
    public string? OrderId { get; set; } = ""; // Razorpay OrderId or Bank Request ID
    public string Status { get; set; } = "Success"; // Success | Pending | Failed | Rejected
    public string? ReceiptUrl { get; set; } = "";
    public string? Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// View model for Tenant Admin Panel Subscription Payment Status page.
/// </summary>
public class SubscriptionPaymentStatusViewModel
{
    public CompanySubscription CurrentSubscription { get; set; } = new();
    public SaaSSubscriptionPlan? CurrentPlan { get; set; }
    public List<SubscriptionPaymentRecord> Payments { get; set; } = new();
    public List<BankTransferRequest> BankRequests { get; set; } = new();
    public string CompanyName { get; set; } = "";
    public string AdminCode { get; set; } = "";
    public decimal TotalPaid { get; set; }
    public int SuccessfulCount { get; set; }
    public int PendingCount { get; set; }
}




