using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrmLeadManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Services;

public class RazorpayOrderResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? OrderId { get; set; }
    public string? KeyId { get; set; }
    public long AmountPaise { get; set; }
    public decimal AmountRupees { get; set; }
    public string Currency { get; set; } = "INR";
    public string PlanName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string ContactPhone { get; set; } = "";
}

public interface ISaaSPaymentService
{
    Task<RazorpayOrderResult> CreateSubscriptionOrderAsync(int adminId, int planId, string billingInterval = "Monthly");
    Task<(bool Success, string Message)> VerifyAndActivateSubscriptionAsync(
        string razorpayOrderId, 
        string razorpayPaymentId, 
        string razorpaySignature, 
        int adminId, 
        int planId);
    Task RecordFailedPaymentAsync(int adminId, int planId, string orderId, string reason, string status = "Failed");
}

public class SaaSPaymentService : ISaaSPaymentService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISaaSEmailService _emailService;
    private readonly ILogger<SaaSPaymentService> _logger;
    private readonly IConfiguration _configuration;

    public SaaSPaymentService(
        AppDbContext db,
        IHttpClientFactory httpClientFactory,
        ISaaSEmailService emailService,
        ILogger<SaaSPaymentService> logger,
        IConfiguration configuration)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _emailService = emailService;
        _logger = logger;
        _configuration = configuration;
    }

    private (string KeyId, string KeySecret) GetCredentials(SaaSPlatformSettings settings)
    {
        var cfgId = _configuration["Razorpay:KeyId"]?.Trim();
        var cfgSecret = _configuration["Razorpay:KeySecret"]?.Trim();

        var keyId = !string.IsNullOrWhiteSpace(settings.RazorpayKeyId)
            ? settings.RazorpayKeyId.Trim()
            : (!string.IsNullOrWhiteSpace(cfgId) ? cfgId : "rzp_test_TzHWqI7Yp67ORT");

        var keySecret = !string.IsNullOrWhiteSpace(settings.RazorpayKeySecret)
            ? settings.RazorpayKeySecret.Trim()
            : (!string.IsNullOrWhiteSpace(cfgSecret) ? cfgSecret : "VnKURIe1Evv8BkDMBY0XS8GC");

        return (keyId, keySecret);
    }

    public async Task<RazorpayOrderResult> CreateSubscriptionOrderAsync(int adminId, int planId, string billingInterval = "Monthly")
    {
        var result = new RazorpayOrderResult();

        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Id == adminId && (u.Role == "Admin" || u.Role == "Super Admin"));
        if (admin == null)
        {
            result.Message = "Company administrator account not found.";
            return result;
        }

        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId && p.IsActive);
        if (plan == null)
        {
            result.Message = "Selected subscription plan is invalid or inactive.";
            return result;
        }

        var settings = await _db.PlatformSettings.FirstOrDefaultAsync() ?? new SaaSPlatformSettings();
        var (keyId, keySecret) = GetCredentials(settings);

        result.PlanName = plan.Name;
        result.CompanyName = admin.CompanyName ?? "Company";
        result.ContactEmail = admin.Email ?? "";
        result.ContactPhone = admin.Phone ?? "";
        bool isYearly = string.Equals(billingInterval, "Yearly", StringComparison.OrdinalIgnoreCase);
        decimal finalAmount = isYearly ? (plan.Price * 12 * 0.8m) : plan.Price;
        result.AmountRupees = finalAmount;
        result.AmountPaise = (long)(finalAmount * 100);
        result.KeyId = keyId;

        // If plan is free trial (price <= 0), no payment gateway required
        if (plan.Price <= 0)
        {
            result.Success = true;
            result.OrderId = $"free_trial_{adminId}_{DateTime.UtcNow.Ticks}";
            return result;
        }

        // Validate Razorpay API keys are configured
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
        {
            result.Success = false;
            result.Message = "Razorpay payment gateway credentials are not configured. Please contact administrator or use Bank Transfer.";
            return result;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var authBytes = Encoding.UTF8.GetBytes($"{keyId}:{keySecret}");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            var orderPayload = new
            {
                amount = result.AmountPaise,
                currency = "INR",
                receipt = $"rcpt_{admin.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}",
                notes = new Dictionary<string, string>
                {
                    { "admin_id", admin.Id.ToString() },
                    { "plan_id", plan.Id.ToString() },
                    { "company_name", result.CompanyName }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(orderPayload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://api.razorpay.com/v1/orders", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseBody);
                result.OrderId = doc.RootElement.GetProperty("id").GetString();
                result.Success = true;
                _logger.LogInformation("Created Razorpay order {OrderId} for {CompanyName} (Admin ID {AdminId}).", result.OrderId, result.CompanyName, adminId);
            }
            else
            {
                result.Message = $"Razorpay API error ({response.StatusCode}): {responseBody}";
                _logger.LogError("Razorpay Order Creation Failed: {Response}", responseBody);
            }
        }
        catch (Exception ex)
        {
            result.Message = $"Exception creating payment order: {ex.Message}";
            _logger.LogError(ex, "Exception while creating Razorpay order for Admin {AdminId}", adminId);
        }

        return result;
    }

    public async Task<(bool Success, string Message)> VerifyAndActivateSubscriptionAsync(
        string razorpayOrderId, 
        string razorpayPaymentId, 
        string razorpaySignature, 
        int adminId, 
        int planId)
    {
        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Id == adminId);
        if (admin == null) return (false, "Administrator not found.");

        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId);
        if (plan == null) return (false, "Subscription plan not found.");

        var settings = await _db.PlatformSettings.FirstOrDefaultAsync() ?? new SaaSPlatformSettings();
        var (_, keySecret) = GetCredentials(settings);

        // Signature Verification: Skip for free trial, verify HMAC SHA256 for paid plans
        bool isValidSignature = false;

        if (plan.Price <= 0 || (razorpayOrderId != null && razorpayOrderId.StartsWith("free_trial_")))
        {
            isValidSignature = true;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(razorpayOrderId) || 
                string.IsNullOrWhiteSpace(razorpayPaymentId) || 
                string.IsNullOrWhiteSpace(razorpaySignature) ||
                string.IsNullOrWhiteSpace(keySecret))
            {
                return (false, "Payment verification requires a valid Razorpay Order ID, Payment ID, and Signature.");
            }

            try
            {
                var payload = $"{razorpayOrderId}|{razorpayPaymentId}";
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(keySecret));
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var generatedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();

                isValidSignature = string.Equals(generatedSignature, razorpaySignature.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Razorpay signature verification exception for Order {OrderId}", razorpayOrderId);
            }
        }

        if (!isValidSignature)
        {
            return (false, "Invalid Razorpay payment signature. Payment cannot be verified.");
        }

        // Prevent duplicate activation for the same Razorpay payment ID
        if (!string.IsNullOrWhiteSpace(razorpayPaymentId))
        {
            var duplicatePayment = await _db.SubscriptionPayments
                .AnyAsync(p => p.TransactionId == razorpayPaymentId && p.Status == "Success");
            if (duplicatePayment)
            {
                return (false, "This payment transaction has already been verified and processed.");
            }
        }

        bool isYearlyOrder = razorpayOrderId.Contains("_yr_") || razorpayOrderId.EndsWith("_yr") || razorpayOrderId.Contains("yearly", StringComparison.OrdinalIgnoreCase);
        decimal paidAmount = isYearlyOrder ? (plan.Price * 12 * 0.8m) : plan.Price;

        // Activate or update CompanySubscription record
        var sub = await _db.CompanySubscriptions.FirstOrDefaultAsync(s => s.AdminId == adminId);
        DateTime baseDate = (sub != null && sub.PlanId == plan.Id && sub.EndDate > DateTime.UtcNow)
            ? sub.EndDate
            : DateTime.UtcNow;
        var calculatedEndDate = isYearlyOrder ? baseDate.AddYears(1) : baseDate.AddMonths(1);

        if (sub == null)
        {
            sub = new CompanySubscription
            {
                AdminId = adminId,
                PlanId = plan.Id,
                PlanName = plan.Name,
                PlanPrice = plan.Price,
                Status = "Active",
                PaymentStatus = "Success",
                StartDate = DateTime.UtcNow,
                EndDate = calculatedEndDate,
                PurchaseDate = DateTime.UtcNow,
                MaxUsers = plan.MaxTeamMembers,
                MaxLeads = plan.MaxLeads,
                AmountPaid = paidAmount,
                PaymentMethod = "Razorpay",
                Notes = $"Paid via Razorpay. Order: {razorpayOrderId}, Payment: {razorpayPaymentId}"
            };
            _db.CompanySubscriptions.Add(sub);
        }
        else
        {
            sub.PlanId = plan.Id;
            sub.PlanName = plan.Name;
            sub.PlanPrice = plan.Price;
            sub.Status = "Active";
            sub.PaymentStatus = "Success";
            sub.StartDate = DateTime.UtcNow;
            sub.EndDate = calculatedEndDate;
            sub.PurchaseDate = DateTime.UtcNow;
            sub.MaxUsers = plan.MaxTeamMembers;
            sub.MaxLeads = plan.MaxLeads;
            sub.AmountPaid = paidAmount;
            sub.PaymentMethod = "Razorpay";
            sub.Notes = $"Paid via Razorpay. Order: {razorpayOrderId}, Payment: {razorpayPaymentId}";
        }

        // Record payment in SubscriptionPayments history
        var paymentRecord = new SubscriptionPaymentRecord
        {
            AdminId = adminId,
            CompanyName = admin.CompanyName ?? "Company",
            AdminCode = admin.AdminCode ?? $"ADM-{adminId:D4}",
            PlanId = plan.Id,
            PlanName = plan.Name,
            BillingInterval = isYearlyOrder ? "Yearly" : "Monthly",
            Amount = paidAmount,
            PaymentMethod = "Razorpay",
            TransactionId = razorpayPaymentId,
            OrderId = razorpayOrderId,
            Status = "Success",
            Notes = $"Razorpay live payment verified. Order: {razorpayOrderId}",
            CreatedAt = DateTime.UtcNow
        };
        _db.SubscriptionPayments.Add(paymentRecord);

        await _db.SaveChangesAsync();

        _logger.LogInformation("Activated Subscription '{PlanName}' for Company '{CompanyName}' (AdminId {AdminId}) via Razorpay.",
            plan.Name, admin.CompanyName, adminId);

        // Dispatch PLAN_ACTIVATED email
        if (!string.IsNullOrWhiteSpace(admin.Email))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendTemplatedEmailAsync(
                        "PLAN_ACTIVATED",
                        admin.Email,
                        admin.Name ?? "Admin",
                        new Dictionary<string, string>
                        {
                            { "CompanyName", admin.CompanyName ?? "Your Company" },
                            { "PlanName", plan.Name },
                            { "LeadLimit", plan.MaxLeads.ToString("N0") },
                            { "UserLimit", plan.MaxTeamMembers.ToString("N0") },
                            { "ExpiryDate", sub.EndDate.ToString("dd MMM yyyy") }
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send PLAN_ACTIVATED email after payment.");
                }
            });
        }

        return (true, $"Subscription to '{plan.Name}' successfully activated.");
    }

    public async Task RecordFailedPaymentAsync(int adminId, int planId, string orderId, string reason, string status = "Failed")
    {
        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Id == adminId);
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId);

        var paymentRecord = new SubscriptionPaymentRecord
        {
            AdminId = adminId,
            CompanyName = admin?.CompanyName ?? "Company",
            AdminCode = admin?.AdminCode ?? $"ADM-{adminId:D4}",
            PlanId = planId,
            PlanName = plan?.Name ?? "Plan",
            BillingInterval = orderId.Contains("_yr") ? "Yearly" : "Monthly",
            Amount = plan?.Price ?? 0m,
            PaymentMethod = "Razorpay",
            TransactionId = "",
            OrderId = orderId,
            Status = string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) ? "Cancelled" : "Failed",
            Notes = $"Payment {status.ToLower()}: {reason}",
            CreatedAt = DateTime.UtcNow
        };
        _db.SubscriptionPayments.Add(paymentRecord);

        var sub = await _db.CompanySubscriptions.FirstOrDefaultAsync(s => s.AdminId == adminId);
        if (sub != null && sub.Status != "Active")
        {
            sub.PaymentStatus = paymentRecord.Status;
        }

        await _db.SaveChangesAsync();
    }
}

