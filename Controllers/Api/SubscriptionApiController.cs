using CrmLeadManagement.Models;
using CrmLeadManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.Api;

[ApiController]
[Route("api/subscription")]
public class SubscriptionApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ISaaSPaymentService _paymentService;
    private readonly ILogger<SubscriptionApiController> _logger;

    public SubscriptionApiController(
        AppDbContext db,
        ISaaSPaymentService paymentService,
        ILogger<SubscriptionApiController> logger)
    {
        _db = db;
        _paymentService = paymentService;
        _logger = logger;
    }

    [HttpGet("plans")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await _db.SubscriptionPlans
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayOrder)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Code,
                p.Price,
                p.BillingInterval,
                maxUsers = p.MaxTeamMembers,
                p.TrialDays,
                p.Description,
                Features = p.Features != null ? p.Features.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : Array.Empty<string>(),
                p.IsPopular
            })
            .ToListAsync();

        return Ok(new { success = true, plans });
    }

    public class CreateOrderRequest
    {
        public int AdminId { get; set; }
        public int PlanId { get; set; }
        public string BillingInterval { get; set; } = "Monthly";
    }

    [HttpPost("create-order")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        if (request.PlanId <= 0)
        {
            return BadRequest(new { success = false, message = "Plan ID is required." });
        }

        var result = await _paymentService.CreateSubscriptionOrderAsync(request.AdminId, request.PlanId, request.BillingInterval);
        if (!result.Success)
        {
            return BadRequest(new { success = false, message = result.Message });
        }

        return Ok(new
        {
            success = true,
            orderId = result.OrderId,
            keyId = result.KeyId,
            amountPaise = result.AmountPaise,
            amountRupees = result.AmountRupees,
            currency = result.Currency,
            planName = result.PlanName,
            companyName = result.CompanyName,
            contactEmail = result.ContactEmail,
            contactPhone = result.ContactPhone
        });
    }

    public class VerifyPaymentRequest
    {
        public string RazorpayOrderId { get; set; } = "";
        public string RazorpayPaymentId { get; set; } = "";
        public string RazorpaySignature { get; set; } = "";
        public int AdminId { get; set; }
        public int PlanId { get; set; }
    }

    [HttpPost("verify-payment")]
    public async Task<IActionResult> VerifyPayment([FromBody] VerifyPaymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RazorpayOrderId) || string.IsNullOrWhiteSpace(request.RazorpayPaymentId))
        {
            return BadRequest(new { success = false, message = "OrderId and PaymentId are required." });
        }

        var (success, message) = await _paymentService.VerifyAndActivateSubscriptionAsync(
            request.RazorpayOrderId,
            request.RazorpayPaymentId,
            request.RazorpaySignature,
            request.AdminId,
            request.PlanId);

        if (!success)
        {
            return BadRequest(new { success = false, message });
        }

        return Ok(new { success = true, message });
    }
}

