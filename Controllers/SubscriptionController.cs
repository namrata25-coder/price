using System.Security.Claims;
using CrmLeadManagement.Models;
using CrmLeadManagement.Security;
using CrmLeadManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers;

[Authorize]
public class SubscriptionController : Controller
{
    private readonly AppDbContext _db;
    private readonly ISaaSPaymentService _paymentService;
    private readonly ISaaSEmailService _emailService;
    private readonly ISubscriptionLimitService _limitService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SubscriptionController> _logger;

    public SubscriptionController(
        AppDbContext db,
        ISaaSPaymentService paymentService,
        ISaaSEmailService emailService,
        ISubscriptionLimitService limitService,
        IWebHostEnvironment env,
        ILogger<SubscriptionController> logger)
    {
        _db = db;
        _paymentService = paymentService;
        _emailService = emailService;
        _limitService = limitService;
        _env = env;
        _logger = logger;
    }

    private (UserRoleEntry? user, int effectiveAdminId, bool isAdmin) GetTenantContext()
    {
        var authEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = _db.Users.FirstOrDefault(u => u.Email == authEmail);
        var effectiveAdminId = currentUser?.GetEffectiveAdminId() ?? currentUser?.Id ?? 0;
        bool isSuperAdmin = PermissionChecker.IsSuperAdmin(User, currentUser);
        bool isAdmin = isSuperAdmin || User.IsInRole("Admin") || (currentUser != null && string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase));
        return (currentUser, effectiveAdminId, isAdmin);
    }

    // GET /Subscription or /Subscription/Index (Plan Cards Overview)
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var (currentUser, effectiveAdminId, isAdmin) = GetTenantContext();
        if (currentUser == null || effectiveAdminId <= 0)
        {
            return RedirectToAction("Login", "Auth");
        }

        ViewData["Title"] = "Subscription & Plans";
        ViewData["ActivePage"] = "AdminSubscription";

        // 1. Fetch active plans ordered by DisplayOrder
        var plans = await _db.SubscriptionPlans
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Price)
            .ToListAsync();

        // 2. Fetch usage metrics and limits via centralized service
        var usage = await _limitService.GetUsageAsync(effectiveAdminId);

        // 3. Current active plan entity
        var currentSub = await _db.CompanySubscriptions.FirstOrDefaultAsync(s => s.AdminId == effectiveAdminId);
        var currentPlan = plans.FirstOrDefault(p => p.Id == (currentSub != null ? currentSub.PlanId : 1))
            ?? plans.FirstOrDefault(p => string.Equals(p.Name, currentSub?.PlanName, StringComparison.OrdinalIgnoreCase));

        // 4. Global platform settings
        var settings = await _db.PlatformSettings.FirstOrDefaultAsync() ?? new SaaSPlatformSettings();

        var vm = new AdminSubscriptionViewModel
        {
            Plans = plans,
            CurrentSubscription = currentSub!,
            CurrentPlan = currentPlan,
            CurrentUsersCount = usage.UsersUsed,
            MaxUsers = usage.MaxUsers,
            CurrentLeadsCount = usage.LeadsUsed,
            MaxLeads = usage.MaxLeads,
            CurrentProjectsCount = usage.ProjectsUsed,
            PlatformSettings = settings,
            CompanyName = currentUser.CompanyName ?? "Company",
            AdminCode = currentUser.AdminCode ?? $"ADM-{effectiveAdminId:D4}",
            IsAdmin = isAdmin,
            PendingBankRequest = usage.PendingBankRequest,
            RecentPayments = usage.RecentPayments
        };

        return View(vm);
    }

    // GET /Subscription/Subscribe/{id} or /Subscription/Checkout/{id} (Checkout & Razorpay Page)
    [HttpGet]
    [Route("Subscription/Subscribe/{id:int}")]
    [Route("Subscription/Checkout/{id:int}")]
    public async Task<IActionResult> Subscribe(int id)
    {
        var (currentUser, effectiveAdminId, isAdmin) = GetTenantContext();
        if (currentUser == null || effectiveAdminId <= 0)
        {
            return RedirectToAction("Login", "Auth");
        }

        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id && p.IsActive);
        if (plan == null)
        {
            TempData["ErrorMessage"] = "Subscription plan not found.";
            return RedirectToAction(nameof(Index));
        }

        var currentSub = await _db.CompanySubscriptions.FirstOrDefaultAsync(s => s.AdminId == effectiveAdminId)
            ?? new CompanySubscription { PlanName = "Free Trial", Status = "Trial" };

        var usersCount = await _db.Users.CountAsync(u => u.AdminId == effectiveAdminId || u.Id == effectiveAdminId);
        var settings = await _db.PlatformSettings.FirstOrDefaultAsync() ?? new SaaSPlatformSettings();

        var featuresList = !string.IsNullOrWhiteSpace(plan.Features)
            ? plan.Features.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : new List<string>();

        var vm = new PlanCheckoutViewModel
        {
            Plan = plan,
            CurrentSubscription = currentSub,
            CurrentUsersCount = usersCount,
            CompanyName = currentUser.CompanyName ?? "Company",
            AdminCode = currentUser.AdminCode ?? $"ADM-{effectiveAdminId:D4}",
            ContactEmail = currentUser.Email ?? "",
            ContactPhone = currentUser.Phone ?? "",
            PlatformSettings = settings,
            Features = featuresList
        };

        ViewData["Title"] = $"Subscribe to {plan.Name} · Pricing Summary";
        ViewData["ActivePage"] = "AdminSubscription";

        return View("Subscribe", vm);
    }

    public class CreateOrderRequestDto
    {
        public int PlanId { get; set; }
        public string BillingInterval { get; set; } = "Monthly";
    }

    // POST /Subscription/CreateRazorpayOrder (AJAX endpoint for Razorpay popup checkout)
    [HttpPost]
    [Route("Subscription/CreateRazorpayOrder")]
    public async Task<IActionResult> CreateRazorpayOrder([FromBody] CreateOrderRequestDto dto)
    {
        var (currentUser, effectiveAdminId, isAdmin) = GetTenantContext();
        if (currentUser == null || effectiveAdminId <= 0)
        {
            return Unauthorized(new { success = false, message = "Session expired. Please log in again." });
        }

        if (!isAdmin)
        {
            return Forbid();
        }

        var orderResult = await _paymentService.CreateSubscriptionOrderAsync(effectiveAdminId, dto.PlanId, dto.BillingInterval);
        if (!orderResult.Success)
        {
            return BadRequest(new { success = false, message = orderResult.Message });
        }

        return Ok(new
        {
            success = true,
            orderId = orderResult.OrderId,
            keyId = orderResult.KeyId,
            amountPaise = orderResult.AmountPaise,
            amountRupees = orderResult.AmountRupees,
            currency = orderResult.Currency,
            planName = orderResult.PlanName,
            companyName = orderResult.CompanyName,
            contactEmail = orderResult.ContactEmail,
            contactPhone = orderResult.ContactPhone
        });
    }

    public class VerifyRazorpayDto
    {
        public string RazorpayOrderId { get; set; } = "";
        public string RazorpayPaymentId { get; set; } = "";
        public string RazorpaySignature { get; set; } = "";
        public int PlanId { get; set; }
    }

    // POST /Subscription/VerifyRazorpayPayment (AJAX verification callback)
    [HttpPost]
    [Route("Subscription/VerifyRazorpayPayment")]
    public async Task<IActionResult> VerifyRazorpayPayment([FromBody] VerifyRazorpayDto dto)
    {
        var (currentUser, effectiveAdminId, isAdmin) = GetTenantContext();
        if (currentUser == null || effectiveAdminId <= 0)
        {
            return Unauthorized(new { success = false, message = "Session expired." });
        }

        var (success, msg) = await _paymentService.VerifyAndActivateSubscriptionAsync(
            dto.RazorpayOrderId,
            dto.RazorpayPaymentId,
            dto.RazorpaySignature,
            effectiveAdminId,
            dto.PlanId);

        if (!success)
        {
            return BadRequest(new { success = false, message = msg });
        }

        TempData["SuccessMessage"] = $"🎉 Successfully subscribed via Razorpay! Your account is now upgraded.";
        return Ok(new { success = true, message = msg, redirectUrl = Url.Action("Index", "Subscription") });
    }

    public class PaymentFailureDto
    {
        public int PlanId { get; set; }
        public string? OrderId { get; set; }
        public string? Reason { get; set; }
        public string? Status { get; set; } // Failed | Cancelled
    }

    // POST /Subscription/RecordPaymentFailure
    [HttpPost]
    [Route("Subscription/RecordPaymentFailure")]
    public async Task<IActionResult> RecordPaymentFailure([FromBody] PaymentFailureDto dto)
    {
        var (currentUser, effectiveAdminId, isAdmin) = GetTenantContext();
        if (currentUser == null || effectiveAdminId <= 0)
        {
            return Unauthorized(new { success = false, message = "Session expired." });
        }

        var status = string.Equals(dto.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ? "Cancelled" : "Failed";
        await _paymentService.RecordFailedPaymentAsync(
            effectiveAdminId,
            dto.PlanId,
            dto.OrderId ?? "",
            dto.Reason ?? "Payment was cancelled or rejected by user/gateway",
            status);

        return Ok(new { success = true, message = $"Payment status recorded as {status}." });
    }

    // POST /Subscription/ConfirmSubscription (Standard Form Submission fallback for Razorpay)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmSubscription(
        int planId, 
        string billingInterval = "Monthly", 
        string couponCode = "",
        string razorpayOrderId = "",
        string razorpayPaymentId = "",
        string razorpaySignature = "")
    {
        var (currentUser, effectiveAdminId, isAdmin) = GetTenantContext();
        if (currentUser == null || effectiveAdminId <= 0)
        {
            TempData["ErrorMessage"] = "Session expired. Please log in again.";
            return RedirectToAction("Login", "Auth");
        }

        if (!isAdmin)
        {
            TempData["ErrorMessage"] = "Access Denied: Only company administrators can manage subscription plans.";
            return RedirectToAction(nameof(Index));
        }

        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId && p.IsActive);
        if (plan == null)
        {
            TempData["ErrorMessage"] = "The selected subscription plan is not available.";
            return RedirectToAction(nameof(Index));
        }

        // Paid plans strictly require online Razorpay payment verification
        if (plan.Price > 0)
        {
            if (string.IsNullOrWhiteSpace(razorpayOrderId) || 
                string.IsNullOrWhiteSpace(razorpayPaymentId) || 
                string.IsNullOrWhiteSpace(razorpaySignature))
            {
                TempData["ErrorMessage"] = "Online payment was not completed. Please complete the payment via Razorpay or submit a Bank Transfer request.";
                return RedirectToAction(nameof(Subscribe), new { id = planId });
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(razorpayOrderId))
            {
                razorpayOrderId = $"free_trial_{effectiveAdminId}_{DateTime.UtcNow.Ticks}";
            }
        }

        var (success, msg) = await _paymentService.VerifyAndActivateSubscriptionAsync(
            razorpayOrderId,
            razorpayPaymentId,
            razorpaySignature,
            effectiveAdminId,
            plan.Id);

        if (!success)
        {
            TempData["ErrorMessage"] = $"Razorpay payment verification failed: {msg}";
            return RedirectToAction(nameof(Subscribe), new { id = planId });
        }

        TempData["SuccessMessage"] = $"🎉 Successfully subscribed to {plan.Name} ({billingInterval}) via Razorpay! Your account is active with {plan.MaxTeamMembers} Users capacity.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Subscription/SubmitBankTransfer (Bank Request Submission with Payment Receipt)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitBankTransfer(
        int planId,
        string billingInterval,
        string? transactionRef,
        string? userNotes,
        IFormFile? receiptFile)
    {
        var (currentUser, effectiveAdminId, isAdmin) = GetTenantContext();
        if (currentUser == null || effectiveAdminId <= 0)
        {
            TempData["ErrorMessage"] = "Session expired. Please log in again.";
            return RedirectToAction("Login", "Auth");
        }

        if (!isAdmin)
        {
            TempData["ErrorMessage"] = "Access Denied: Only company administrators can manage subscription plans.";
            return RedirectToAction(nameof(Index));
        }

        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId && p.IsActive);
        if (plan == null)
        {
            TempData["ErrorMessage"] = "The selected subscription plan is not available.";
            return RedirectToAction(nameof(Index));
        }

        decimal amount = string.Equals(billingInterval, "Yearly", StringComparison.OrdinalIgnoreCase)
            ? (plan.Price * 12 * 0.8m)
            : plan.Price;

        string? relativePath = null;
        if (receiptFile != null && receiptFile.Length > 0)
        {
            var allowedExtensions = new[] { ".png", ".jpg", ".jpeg", ".pdf", ".webp" };
            var ext = Path.GetExtension(receiptFile.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext))
            {
                TempData["ErrorMessage"] = "Invalid receipt file format. Please upload an image (.png, .jpg) or PDF.";
                return RedirectToAction(nameof(Subscribe), new { id = planId });
            }

            var uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "bank_receipts");
            Directory.CreateDirectory(uploadDir);

            var fileName = $"receipt_{effectiveAdminId}_{DateTime.UtcNow.Ticks}{ext}";
            var physicalPath = Path.Combine(uploadDir, fileName);

            using (var stream = new FileStream(physicalPath, FileMode.Create))
            {
                await receiptFile.CopyToAsync(stream);
            }

            relativePath = $"/uploads/bank_receipts/{fileName}";
        }

        var bankRequest = new BankTransferRequest
        {
            AdminId = effectiveAdminId,
            CompanyName = currentUser.CompanyName ?? "Company",
            AdminCode = currentUser.AdminCode ?? $"ADM-{effectiveAdminId:D4}",
            ContactEmail = currentUser.Email ?? "",
            ContactPhone = currentUser.Phone ?? "",
            PlanId = plan.Id,
            PlanName = plan.Name,
            Amount = amount,
            BillingInterval = string.Equals(billingInterval, "Yearly", StringComparison.OrdinalIgnoreCase) ? "Yearly" : "Monthly",
            ReceiptFilePath = relativePath,
            TransactionReference = transactionRef?.Trim(),
            UserNotes = userNotes?.Trim(),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        _db.BankTransferRequests.Add(bankRequest);

        var paymentRecord = new SubscriptionPaymentRecord
        {
            AdminId = effectiveAdminId,
            CompanyName = currentUser.CompanyName ?? "Company",
            AdminCode = currentUser.AdminCode ?? $"ADM-{effectiveAdminId:D4}",
            PlanId = plan.Id,
            PlanName = plan.Name,
            BillingInterval = string.Equals(billingInterval, "Yearly", StringComparison.OrdinalIgnoreCase) ? "Yearly" : "Monthly",
            Amount = amount,
            PaymentMethod = "Bank Transfer",
            TransactionId = transactionRef?.Trim(),
            OrderId = $"BANK-REQ-TEMP",
            Status = "Pending",
            ReceiptUrl = relativePath,
            Notes = userNotes?.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.SubscriptionPayments.Add(paymentRecord);

        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"🏦 Bank transfer request submitted successfully for {plan.Name}! SuperAdmin will verify your payment receipt and activate your plan.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Subscription/StartTrial
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartTrial(int planId)
    {
        var (currentUser, effectiveAdminId, isAdmin) = GetTenantContext();
        if (currentUser == null || effectiveAdminId <= 0)
        {
            TempData["ErrorMessage"] = "Session expired. Please log in again.";
            return RedirectToAction("Login", "Auth");
        }

        if (!isAdmin)
        {
            TempData["ErrorMessage"] = "Access Denied: Only company administrators can activate trials.";
            return RedirectToAction(nameof(Index));
        }

        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId && p.IsActive);
        if (plan == null)
        {
            TempData["ErrorMessage"] = "The selected subscription plan is not available.";
            return RedirectToAction(nameof(Index));
        }

        int trialDays = plan.TrialDays > 0 ? plan.TrialDays : 7;
        var newEndDate = DateTime.UtcNow.AddDays(trialDays);

        int trialMaxUsers = plan.MaxTeamMembers > 0 ? plan.MaxTeamMembers : 3;
        if (plan.Name.Contains("Pro", StringComparison.OrdinalIgnoreCase)) trialMaxUsers = 15;
        else if (plan.Name.Contains("Starter", StringComparison.OrdinalIgnoreCase)) trialMaxUsers = 5;

        var sub = await _db.CompanySubscriptions.FirstOrDefaultAsync(s => s.AdminId == effectiveAdminId);
        if (sub == null)
        {
            sub = new CompanySubscription
            {
                AdminId = effectiveAdminId,
                PlanId = plan.Id,
                PlanName = plan.Name,
                Status = "Trial",
                StartDate = DateTime.UtcNow,
                EndDate = newEndDate,
                MaxUsers = trialMaxUsers,
                MaxLeads = plan.MaxLeads > 0 ? plan.MaxLeads : 100,
                AmountPaid = 0m,
                PaymentMethod = "Free Trial",
                Notes = $"Activated {trialDays}-day trial for {plan.Name} on {DateTime.UtcNow:dd MMM yyyy}"
            };
            _db.CompanySubscriptions.Add(sub);
        }
        else
        {
            sub.PlanId = plan.Id;
            sub.PlanName = plan.Name;
            sub.Status = "Trial";
            sub.StartDate = DateTime.UtcNow;
            sub.EndDate = newEndDate;
            sub.MaxUsers = trialMaxUsers;
            sub.MaxLeads = plan.MaxLeads > 0 ? plan.MaxLeads : 100;
            sub.Notes = $"Activated {trialDays}-day trial for {plan.Name} on {DateTime.UtcNow:dd MMM yyyy}";
        }

        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"⏱️ Started {trialDays}-day free trial for '{plan.Name}'! Valid until {newEndDate:dd MMM yyyy}.";
        return RedirectToAction(nameof(Index));
    }

    // GET /Subscription/PaymentStatus (Loads subscription payment history & transaction status)
    [HttpGet]
    [Route("Subscription/PaymentStatus")]
    public async Task<IActionResult> PaymentStatus()
    {
        var (currentUser, effectiveAdminId, isAdmin) = GetTenantContext();
        if (currentUser == null || effectiveAdminId <= 0)
        {
            return RedirectToAction("Login", "Auth");
        }

        ViewData["Title"] = "Subscription Payment Status · Billing";
        ViewData["ActivePage"] = "AdminPaymentStatus";

        // 1. Current Subscription & Plan
        var sub = await _db.CompanySubscriptions.FirstOrDefaultAsync(s => s.AdminId == effectiveAdminId);
        var plan = sub != null ? await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == sub.PlanId) : null;

        // 2. Fetch Subscription Payment Transactions
        var payments = await _db.SubscriptionPayments
            .Where(p => p.AdminId == effectiveAdminId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        // 3. Fetch Bank Transfer Offline Requests
        var bankRequests = await _db.BankTransferRequests
            .Where(r => r.AdminId == effectiveAdminId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        // If no records in SubscriptionPayments yet, seed from existing subscription
        if (!payments.Any() && sub != null)
        {
            var seedPayment = new SubscriptionPaymentRecord
            {
                AdminId = effectiveAdminId,
                CompanyName = currentUser.CompanyName ?? "Company",
                AdminCode = currentUser.AdminCode ?? $"ADM-{effectiveAdminId:D4}",
                PlanId = sub.PlanId,
                PlanName = sub.PlanName,
                BillingInterval = sub.Notes?.Contains("Yearly") == true ? "Yearly" : "Monthly",
                Amount = sub.AmountPaid,
                PaymentMethod = sub.PaymentMethod,
                TransactionId = sub.Notes?.Contains("Payment: ") == true ? sub.Notes.Substring(sub.Notes.IndexOf("Payment: ") + 9) : "INITIAL_SUB",
                OrderId = sub.Notes?.Contains("Order: ") == true ? sub.Notes.Substring(sub.Notes.IndexOf("Order: ") + 7, 20) : "ORD_INIT",
                Status = string.Equals(sub.Status, "Active", StringComparison.OrdinalIgnoreCase) ? "Success" : sub.Status,
                Notes = sub.Notes,
                CreatedAt = sub.CreatedAt
            };
            payments.Add(seedPayment);
            _db.SubscriptionPayments.Add(seedPayment);
            await _db.SaveChangesAsync();
        }

        var vm = new SubscriptionPaymentStatusViewModel
        {
            CurrentSubscription = sub ?? new CompanySubscription { PlanName = "Free Trial", Status = "Trial" },
            CurrentPlan = plan,
            Payments = payments,
            BankRequests = bankRequests,
            CompanyName = currentUser.CompanyName ?? "Company",
            AdminCode = currentUser.AdminCode ?? $"ADM-{effectiveAdminId:D4}",
            TotalPaid = payments.Where(p => p.Status == "Success").Sum(p => p.Amount),
            SuccessfulCount = payments.Count(p => p.Status == "Success"),
            PendingCount = payments.Count(p => p.Status == "Pending") + bankRequests.Count(r => r.Status == "Pending")
        };

        return View(vm);
    }
}
