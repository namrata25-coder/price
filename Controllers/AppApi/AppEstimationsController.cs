using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos.App;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.AppApi;

/// <summary>
/// REST API endpoints for Estimations under /api/v1/app/estimations and /api/app/estimations.
/// </summary>
[ApiController]
[Route("api/v1/app/estimations")]
[Route("api/app/estimations")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class AppEstimationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AppEstimationsController> _logger;

    public AppEstimationsController(AppDbContext db, ILogger<AppEstimationsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private static decimal ParseDec(string? s) => decimal.TryParse(s, out var v) ? v : 0m;

    private async Task<string> NextEstimationNumberAsync()
    {
        var numbers = await _db.Estimations.Select(e => e.Number).ToListAsync();
        var maxNum = numbers
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.StartsWith("EST-", StringComparison.OrdinalIgnoreCase) ? n.Substring(4) : n)
            .Select(n => int.TryParse(n, out var v) ? v : 2000)
            .DefaultIfEmpty(2000)
            .Max();

        int next = Math.Max(maxNum + 1, 2001);
        while (numbers.Any(n => string.Equals(n, $"EST-{next}", StringComparison.OrdinalIgnoreCase)))
        {
            next++;
        }
        return $"EST-{next}";
    }

    /// <summary>
    /// GET /api/v1/app/estimations
    /// Retrieves a paginated list of estimations for the current user/tenant.
    /// Supports filtering by leadId, status, search keyword, and pagination.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetEstimations(
        [FromQuery] string? leadId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var leadsQuery = LeadDataScope.ScopedLeadsQuery(_db, User).AsNoTracking();

            if (!string.IsNullOrWhiteSpace(leadId))
            {
                var lid = leadId.Trim();
                leadsQuery = leadsQuery.Where(l => l.LeadId == lid || l.Id.ToString() == lid);
            }

            var leads = await leadsQuery.ToListAsync();

            var list = new List<AppEstimationResponseDto>();
            foreach (var lead in leads)
            {
                if (lead.Estimations == null) continue;

                foreach (var est in lead.Estimations)
                {
                    list.Add(new AppEstimationResponseDto
                    {
                        Id = est.Id,
                        AdminId = est.AdminId ?? lead.AdminId,
                        Number = est.Number ?? "",
                        LeadId = lead.LeadId ?? "",
                        LeadName = lead.LeadName ?? "",
                        CompanyName = lead.CompanyName ?? "",
                        LeadCustomer = !string.IsNullOrWhiteSpace(est.LeadCustomer) 
                            ? est.LeadCustomer 
                            : $"{lead.LeadName} ({(string.IsNullOrWhiteSpace(lead.CompanyName) ? "Individual" : lead.CompanyName)})",
                        Date = est.Date ?? "",
                        ProjectRequirement = est.ProjectRequirement ?? "",
                        Items = (est.Items ?? new List<FinanceLineItem>()).Select(i => new AppFinanceLineItemDto
                        {
                            Id = i.Id,
                            ItemName = i.ItemName ?? "",
                            Quantity = i.Quantity ?? "1",
                            Rate = i.Rate ?? "0",
                            Amount = i.Amount ?? "0"
                        }).ToList(),
                        Subtotal = est.Subtotal ?? "0",
                        Discount = est.Discount ?? "0",
                        TaxRate = est.TaxRate ?? "0",
                        Tax = est.Tax ?? "0",
                        TaxName = est.TaxName ?? "GST",
                        TaxType = est.TaxType ?? "CGST/SGST",
                        Total = est.Total ?? "0",
                        ValidUntil = est.ValidUntil ?? "",
                        TermsConditions = est.TermsConditions ?? "",
                        Notes = est.Notes ?? "",
                        Status = est.Status ?? "Draft",
                        AttachmentPath = est.AttachmentPath,
                        AttachmentFileName = est.AttachmentFileName
                    });
                }
            }

            // Filtering by status
            if (!string.IsNullOrWhiteSpace(status))
            {
                list = list.Where(e => string.Equals(e.Status, status.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // Search filtering
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                list = list.Where(e =>
                    e.Number.ToLower().Contains(s) ||
                    e.LeadCustomer.ToLower().Contains(s) ||
                    e.LeadName.ToLower().Contains(s) ||
                    e.CompanyName.ToLower().Contains(s) ||
                    e.ProjectRequirement.ToLower().Contains(s) ||
                    e.Notes.ToLower().Contains(s)).ToList();
            }

            list = list.OrderByDescending(e => e.Id).ToList();

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var totalCount = list.Count;
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var pagedItems = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var result = new PaginatedResult<AppEstimationResponseDto>
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Items = pagedItems
            };

            return Ok(AppApiResponse<PaginatedResult<AppEstimationResponseDto>>.Ok(result, "Estimations retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving estimations in AppApi.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving estimations."));
        }
    }

    /// <summary>
    /// GET /api/v1/app/estimations/{idOrNumber}
    /// Retrieves a single estimation by numeric Id or code number (e.g. EST-2001).
    /// </summary>
    [HttpGet("{idOrNumber}")]
    public async Task<IActionResult> GetEstimationById(string idOrNumber)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(idOrNumber))
            {
                return BadRequest(AppApiErrorResponse.Fail("Estimation identifier is required."));
            }

            var idTrim = idOrNumber.Trim();
            int.TryParse(idTrim, out var numericId);

            var leads = await LeadDataScope.ScopedLeadsQuery(_db, User).AsNoTracking().ToListAsync();
            foreach (var lead in leads)
            {
                if (lead.Estimations == null) continue;

                var est = lead.Estimations.FirstOrDefault(e => 
                    (numericId > 0 && e.Id == numericId) || 
                    string.Equals(e.Number, idTrim, StringComparison.OrdinalIgnoreCase));

                if (est != null)
                {
                    var dto = new AppEstimationResponseDto
                    {
                        Id = est.Id,
                        AdminId = est.AdminId ?? lead.AdminId,
                        Number = est.Number ?? "",
                        LeadId = lead.LeadId ?? "",
                        LeadName = lead.LeadName ?? "",
                        CompanyName = lead.CompanyName ?? "",
                        LeadCustomer = !string.IsNullOrWhiteSpace(est.LeadCustomer) 
                            ? est.LeadCustomer 
                            : $"{lead.LeadName} ({(string.IsNullOrWhiteSpace(lead.CompanyName) ? "Individual" : lead.CompanyName)})",
                        Date = est.Date ?? "",
                        ProjectRequirement = est.ProjectRequirement ?? "",
                        Items = (est.Items ?? new List<FinanceLineItem>()).Select(i => new AppFinanceLineItemDto
                        {
                            Id = i.Id,
                            ItemName = i.ItemName ?? "",
                            Quantity = i.Quantity ?? "1",
                            Rate = i.Rate ?? "0",
                            Amount = i.Amount ?? "0"
                        }).ToList(),
                        Subtotal = est.Subtotal ?? "0",
                        Discount = est.Discount ?? "0",
                        TaxRate = est.TaxRate ?? "0",
                        Tax = est.Tax ?? "0",
                        TaxName = est.TaxName ?? "GST",
                        TaxType = est.TaxType ?? "CGST/SGST",
                        Total = est.Total ?? "0",
                        ValidUntil = est.ValidUntil ?? "",
                        TermsConditions = est.TermsConditions ?? "",
                        Notes = est.Notes ?? "",
                        Status = est.Status ?? "Draft",
                        AttachmentPath = est.AttachmentPath,
                        AttachmentFileName = est.AttachmentFileName
                    };

                    return Ok(AppApiResponse<AppEstimationResponseDto>.Ok(dto, "Estimation details retrieved successfully."));
                }
            }

            return NotFound(AppApiErrorResponse.Fail($"Estimation '{idOrNumber}' was not found or access is denied."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving estimation {IdOrNumber} in AppApi.", idOrNumber);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving estimation details."));
        }
    }

    /// <summary>
    /// POST /api/v1/app/estimations
    /// Creates a new Estimation against a lead.
    /// Automatically calculates item amounts, subtotal, tax, discount, total, and assigns EST-xxxx number.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateEstimation([FromBody] AppCreateEstimationRequest request)
    {
        try
        {
            if (request == null)
            {
                return BadRequest(AppApiErrorResponse.Fail("Request payload is required."));
            }

            if (string.IsNullOrWhiteSpace(request.LeadId))
            {
                return BadRequest(AppApiErrorResponse.Fail("LeadId is required to create an estimation."));
            }

            var leadIdTrim = request.LeadId.Trim();
            var lead = await LeadDataScope.ScopedLeadsQuery(_db, User)
                .Include(l => l.Estimations)
                .FirstOrDefaultAsync(l => l.LeadId == leadIdTrim || l.Id.ToString() == leadIdTrim);

            if (lead == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Lead '{request.LeadId}' was not found or access is denied."));
            }

            // Build line items and calculate amounts
            var rawItems = request.Items ?? new List<AppFinanceLineItemDto>();
            if (rawItems.Count == 0)
            {
                rawItems.Add(new AppFinanceLineItemDto
                {
                    ItemName = !string.IsNullOrWhiteSpace(request.ProjectRequirement) ? request.ProjectRequirement : "Standard Estimation Item",
                    Quantity = "1",
                    Rate = "0",
                    Amount = "0"
                });
            }

            var lineItems = new List<FinanceLineItem>();
            foreach (var item in rawItems)
            {
                var qty = ParseDec(item.Quantity);
                if (qty <= 0) qty = 1;
                var rate = ParseDec(item.Rate);
                var amount = qty * rate;

                lineItems.Add(new FinanceLineItem
                {
                    ItemName = !string.IsNullOrWhiteSpace(item.ItemName) ? item.ItemName : "Estimation Item",
                    Quantity = qty.ToString("0.##"),
                    Rate = rate.ToString("0.##"),
                    Amount = amount.ToString("0.##")
                });
            }

            var subtotal = lineItems.Sum(i => ParseDec(i.Amount));
            var discountVal = ParseDec(request.Discount);
            var taxable = Math.Max(0m, subtotal - discountVal);
            var rateVal = ParseDec(request.TaxRate);
            var taxVal = rateVal > 0 ? (taxable * (rateVal / 100m)) : ParseDec(request.Tax);
            var total = taxable + taxVal;

            var number = await NextEstimationNumberAsync();

            var entry = new EstimationEntry
            {
                Number = number,
                Date = !string.IsNullOrWhiteSpace(request.Date) ? request.Date.Trim() : DateTime.UtcNow.ToString("yyyy-MM-dd"),
                LeadCustomer = $"{lead.LeadName} ({(string.IsNullOrWhiteSpace(lead.CompanyName) ? "Individual" : lead.CompanyName)})",
                ProjectRequirement = request.ProjectRequirement ?? "",
                Items = lineItems,
                Subtotal = subtotal.ToString("0.##"),
                Discount = discountVal.ToString("0.##"),
                TaxRate = rateVal.ToString("0.##"),
                Tax = taxVal.ToString("0.##"),
                TaxName = !string.IsNullOrWhiteSpace(request.TaxName) ? request.TaxName : "GST",
                TaxType = !string.IsNullOrWhiteSpace(request.TaxType) ? request.TaxType : "CGST/SGST",
                Total = total.ToString("0.##"),
                ValidUntil = request.ValidUntil ?? "",
                TermsConditions = request.TermsConditions ?? "",
                Notes = request.Notes ?? "",
                Status = !string.IsNullOrWhiteSpace(request.Status) ? request.Status : "Draft",
                AdminId = lead.AdminId
            };

            lead.Estimations.Add(entry);
            await _db.SaveChangesAsync();

            var dto = new AppEstimationResponseDto
            {
                Id = entry.Id,
                AdminId = entry.AdminId,
                Number = entry.Number,
                LeadId = lead.LeadId ?? "",
                LeadName = lead.LeadName ?? "",
                CompanyName = lead.CompanyName ?? "",
                LeadCustomer = entry.LeadCustomer,
                Date = entry.Date ?? "",
                ProjectRequirement = entry.ProjectRequirement ?? "",
                Items = lineItems.Select(i => new AppFinanceLineItemDto
                {
                    Id = i.Id,
                    ItemName = i.ItemName,
                    Quantity = i.Quantity,
                    Rate = i.Rate,
                    Amount = i.Amount
                }).ToList(),
                Subtotal = entry.Subtotal ?? "0",
                Discount = entry.Discount ?? "0",
                TaxRate = entry.TaxRate ?? "0",
                Tax = entry.Tax ?? "0",
                TaxName = entry.TaxName ?? "",
                TaxType = entry.TaxType ?? "",
                Total = entry.Total ?? "0",
                ValidUntil = entry.ValidUntil ?? "",
                TermsConditions = entry.TermsConditions ?? "",
                Notes = entry.Notes ?? "",
                Status = entry.Status ?? "Draft"
            };

            return Created($"/api/v1/app/estimations/{entry.Number}", 
                AppApiResponse<AppEstimationResponseDto>.Ok(dto, "Estimation created successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating estimation in AppApi.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while creating the estimation."));
        }
    }
}

