using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos.App;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.AppApi;

/// <summary>
/// REST API endpoints for Quotations under /api/v1/app/quotations and /api/app/quotations.
/// </summary>
[ApiController]
[Route("api/v1/app/quotations")]
[Route("api/app/quotations")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class AppQuotationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AppQuotationsController> _logger;

    public AppQuotationsController(AppDbContext db, ILogger<AppQuotationsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private static decimal ParseDec(string? s) => decimal.TryParse(s, out var v) ? v : 0m;

    private async Task<string> NextQuotationNumberAsync()
    {
        var numbers = await _db.Quotations.Select(q => q.Number).ToListAsync();
        var maxNum = numbers
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.StartsWith("QUO-", StringComparison.OrdinalIgnoreCase) ? n.Substring(4) : n)
            .Select(n => int.TryParse(n, out var v) ? v : 2000)
            .DefaultIfEmpty(2000)
            .Max();

        int next = Math.Max(maxNum + 1, 2001);
        while (numbers.Any(n => string.Equals(n, $"QUO-{next}", StringComparison.OrdinalIgnoreCase)))
        {
            next++;
        }
        return $"QUO-{next}";
    }

    /// <summary>
    /// GET /api/v1/app/quotations
    /// Retrieves a paginated list of quotations for the current user/tenant.
    /// Supports filtering by leadId, status, search keyword, and pagination.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetQuotations(
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

            var list = new List<AppQuotationResponseDto>();
            foreach (var lead in leads)
            {
                if (lead.Quotations == null) continue;

                foreach (var quo in lead.Quotations)
                {
                    list.Add(new AppQuotationResponseDto
                    {
                        Id = quo.Id,
                        AdminId = quo.AdminId ?? lead.AdminId,
                        Number = quo.Number ?? "",
                        LeadId = lead.LeadId ?? "",
                        LeadName = lead.LeadName ?? "",
                        CompanyName = lead.CompanyName ?? "",
                        LeadCustomer = !string.IsNullOrWhiteSpace(quo.LeadCustomer)
                            ? quo.LeadCustomer
                            : $"{lead.LeadName} ({(string.IsNullOrWhiteSpace(lead.CompanyName) ? "Individual" : lead.CompanyName)})",
                        Date = quo.Date ?? "",
                        ProjectRequirement = quo.ProjectRequirement ?? "",
                        Items = (quo.Items ?? new List<FinanceLineItem>()).Select(i => new AppFinanceLineItemDto
                        {
                            Id = i.Id,
                            ItemName = i.ItemName ?? "",
                            Quantity = i.Quantity ?? "1",
                            Rate = i.Rate ?? "0",
                            Amount = i.Amount ?? "0"
                        }).ToList(),
                        Subtotal = quo.Subtotal ?? "0",
                        Discount = quo.Discount ?? "0",
                        TaxRate = quo.TaxRate ?? "0",
                        Tax = quo.Tax ?? "0",
                        TaxName = quo.TaxName ?? "GST",
                        TaxType = quo.TaxType ?? "CGST/SGST",
                        GrandTotal = quo.GrandTotal ?? "0",
                        ValidUntil = quo.ValidUntil ?? "",
                        TermsConditions = quo.TermsConditions ?? "",
                        Notes = quo.Notes ?? "",
                        Status = quo.Status ?? "Draft",
                        AttachmentPath = quo.AttachmentPath,
                        AttachmentFileName = quo.AttachmentFileName
                    });
                }
            }

            // Filtering by status
            if (!string.IsNullOrWhiteSpace(status))
            {
                list = list.Where(q => string.Equals(q.Status, status.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // Search filtering
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                list = list.Where(q =>
                    q.Number.ToLower().Contains(s) ||
                    q.LeadCustomer.ToLower().Contains(s) ||
                    q.LeadName.ToLower().Contains(s) ||
                    q.CompanyName.ToLower().Contains(s) ||
                    q.ProjectRequirement.ToLower().Contains(s) ||
                    q.Notes.ToLower().Contains(s)).ToList();
            }

            list = list.OrderByDescending(q => q.Id).ToList();

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var totalCount = list.Count;
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var pagedItems = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var result = new PaginatedResult<AppQuotationResponseDto>
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Items = pagedItems
            };

            return Ok(AppApiResponse<PaginatedResult<AppQuotationResponseDto>>.Ok(result, "Quotations retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving quotations in AppApi.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving quotations."));
        }
    }

    /// <summary>
    /// GET /api/v1/app/quotations/{idOrNumber}
    /// Retrieves a single quotation by numeric Id or code number (e.g. QUO-2001).
    /// </summary>
    [HttpGet("{idOrNumber}")]
    public async Task<IActionResult> GetQuotationById(string idOrNumber)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(idOrNumber))
            {
                return BadRequest(AppApiErrorResponse.Fail("Quotation identifier is required."));
            }

            var idTrim = idOrNumber.Trim();
            int.TryParse(idTrim, out var numericId);

            var leads = await LeadDataScope.ScopedLeadsQuery(_db, User).AsNoTracking().ToListAsync();
            foreach (var lead in leads)
            {
                if (lead.Quotations == null) continue;

                var quo = lead.Quotations.FirstOrDefault(q =>
                    (numericId > 0 && q.Id == numericId) ||
                    string.Equals(q.Number, idTrim, StringComparison.OrdinalIgnoreCase));

                if (quo != null)
                {
                    var dto = new AppQuotationResponseDto
                    {
                        Id = quo.Id,
                        AdminId = quo.AdminId ?? lead.AdminId,
                        Number = quo.Number ?? "",
                        LeadId = lead.LeadId ?? "",
                        LeadName = lead.LeadName ?? "",
                        CompanyName = lead.CompanyName ?? "",
                        LeadCustomer = !string.IsNullOrWhiteSpace(quo.LeadCustomer)
                            ? quo.LeadCustomer
                            : $"{lead.LeadName} ({(string.IsNullOrWhiteSpace(lead.CompanyName) ? "Individual" : lead.CompanyName)})",
                        Date = quo.Date ?? "",
                        ProjectRequirement = quo.ProjectRequirement ?? "",
                        Items = (quo.Items ?? new List<FinanceLineItem>()).Select(i => new AppFinanceLineItemDto
                        {
                            Id = i.Id,
                            ItemName = i.ItemName ?? "",
                            Quantity = i.Quantity ?? "1",
                            Rate = i.Rate ?? "0",
                            Amount = i.Amount ?? "0"
                        }).ToList(),
                        Subtotal = quo.Subtotal ?? "0",
                        Discount = quo.Discount ?? "0",
                        TaxRate = quo.TaxRate ?? "0",
                        Tax = quo.Tax ?? "0",
                        TaxName = quo.TaxName ?? "GST",
                        TaxType = quo.TaxType ?? "CGST/SGST",
                        GrandTotal = quo.GrandTotal ?? "0",
                        ValidUntil = quo.ValidUntil ?? "",
                        TermsConditions = quo.TermsConditions ?? "",
                        Notes = quo.Notes ?? "",
                        Status = quo.Status ?? "Draft",
                        AttachmentPath = quo.AttachmentPath,
                        AttachmentFileName = quo.AttachmentFileName
                    };

                    return Ok(AppApiResponse<AppQuotationResponseDto>.Ok(dto, "Quotation details retrieved successfully."));
                }
            }

            return NotFound(AppApiErrorResponse.Fail($"Quotation '{idOrNumber}' was not found or access is denied."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving quotation {IdOrNumber} in AppApi.", idOrNumber);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving quotation details."));
        }
    }

    /// <summary>
    /// POST /api/v1/app/quotations
    /// Creates a new Quotation against a lead.
    /// Automatically calculates item amounts, subtotal, tax, discount, grand total, and assigns QUO-xxxx number.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateQuotation([FromBody] AppCreateQuotationRequest request)
    {
        try
        {
            if (request == null)
            {
                return BadRequest(AppApiErrorResponse.Fail("Request payload is required."));
            }

            if (string.IsNullOrWhiteSpace(request.LeadId))
            {
                return BadRequest(AppApiErrorResponse.Fail("LeadId is required to create a quotation."));
            }

            var leadIdTrim = request.LeadId.Trim();
            var lead = await LeadDataScope.ScopedLeadsQuery(_db, User)
                .Include(l => l.Quotations)
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
                    ItemName = !string.IsNullOrWhiteSpace(request.ProjectRequirement) ? request.ProjectRequirement : "Standard Quotation Item",
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
                    ItemName = !string.IsNullOrWhiteSpace(item.ItemName) ? item.ItemName : "Quotation Item",
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
            var grandTotal = taxable + taxVal;

            var number = await NextQuotationNumberAsync();

            var entry = new QuotationEntry
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
                GrandTotal = grandTotal.ToString("0.##"),
                ValidUntil = request.ValidUntil ?? "",
                TermsConditions = request.TermsConditions ?? "",
                Notes = request.Notes ?? "",
                Status = !string.IsNullOrWhiteSpace(request.Status) ? request.Status : "Draft",
                AdminId = lead.AdminId
            };

            lead.Quotations.Add(entry);
            await _db.SaveChangesAsync();

            var dto = new AppQuotationResponseDto
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
                GrandTotal = entry.GrandTotal ?? "0",
                ValidUntil = entry.ValidUntil ?? "",
                TermsConditions = entry.TermsConditions ?? "",
                Notes = entry.Notes ?? "",
                Status = entry.Status ?? "Draft"
            };

            return Created($"/api/v1/app/quotations/{entry.Number}",
                AppApiResponse<AppQuotationResponseDto>.Ok(dto, "Quotation created successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating quotation in AppApi.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while creating the quotation."));
        }
    }
}

