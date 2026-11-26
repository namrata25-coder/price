using CrmLeadManagement.Models;
using CrmLeadManagement.Models.Dtos.App;
using CrmLeadManagement.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Controllers.AppApi;

/// <summary>
/// REST API endpoints for Proposals under /api/v1/app/proposals and /api/app/proposals.
/// </summary>
[ApiController]
[Route("api/v1/app/proposals")]
[Route("api/app/proposals")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + BearerTokenAuthenticationHandler.SchemeName)]
public class AppProposalsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AppProposalsController> _logger;

    public AppProposalsController(AppDbContext db, ILogger<AppProposalsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private static decimal ParseDec(string? s) => decimal.TryParse(s, out var v) ? v : 0m;

    private async Task<string> NextProposalNumberAsync()
    {
        var numbers = await _db.Proposals.Select(p => p.Number).ToListAsync();
        var maxNum = numbers
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.StartsWith("PRO-", StringComparison.OrdinalIgnoreCase) ? n.Substring(4) : n)
            .Select(n => int.TryParse(n, out var v) ? v : 2000)
            .DefaultIfEmpty(2000)
            .Max();

        int next = Math.Max(maxNum + 1, 2001);
        while (numbers.Any(n => string.Equals(n, $"PRO-{next}", StringComparison.OrdinalIgnoreCase)))
        {
            next++;
        }
        return $"PRO-{next}";
    }

    /// <summary>
    /// GET /api/v1/app/proposals
    /// Retrieves a paginated list of proposals for the current user/tenant.
    /// Supports filtering by leadId, status, search keyword, and pagination.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetProposals(
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

            var list = new List<AppProposalResponseDto>();
            foreach (var lead in leads)
            {
                if (lead.Proposals == null) continue;

                foreach (var pro in lead.Proposals)
                {
                    list.Add(new AppProposalResponseDto
                    {
                        Id = pro.Id,
                        AdminId = pro.AdminId ?? lead.AdminId,
                        Number = pro.Number ?? "",
                        LeadId = lead.LeadId ?? "",
                        LeadName = lead.LeadName ?? "",
                        CompanyName = lead.CompanyName ?? "",
                        LeadCustomer = !string.IsNullOrWhiteSpace(pro.LeadCustomer)
                            ? pro.LeadCustomer
                            : $"{lead.LeadName} ({(string.IsNullOrWhiteSpace(lead.CompanyName) ? "Individual" : lead.CompanyName)})",
                        Title = !string.IsNullOrWhiteSpace(pro.Title) ? pro.Title : "Proposal",
                        Date = pro.Date ?? "",
                        ProjectRequirement = pro.ProjectRequirement ?? "",
                        ScopeOfWork = pro.ScopeOfWork ?? "",
                        Description = pro.Description ?? "",
                        Deliverables = pro.Deliverables ?? "",
                        Timeline = pro.Timeline ?? "",
                        Budget = pro.Budget ?? "",
                        Subtotal = pro.Subtotal ?? "0",
                        Discount = pro.Discount ?? "0",
                        TaxRate = pro.TaxRate ?? "0",
                        Tax = pro.Tax ?? "0",
                        TaxName = pro.TaxName ?? "GST",
                        TaxType = pro.TaxType ?? "CGST/SGST",
                        GrandTotal = pro.GrandTotal ?? "0",
                        TermsConditions = pro.TermsConditions ?? "",
                        Notes = pro.Notes ?? "",
                        Status = pro.Status ?? "Draft",
                        AttachmentPath = pro.AttachmentPath,
                        AttachmentFileName = pro.AttachmentFileName
                    });
                }
            }

            // Filtering by status
            if (!string.IsNullOrWhiteSpace(status))
            {
                list = list.Where(p => string.Equals(p.Status, status.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // Search filtering
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                list = list.Where(p =>
                    p.Number.ToLower().Contains(s) ||
                    p.Title.ToLower().Contains(s) ||
                    p.LeadCustomer.ToLower().Contains(s) ||
                    p.LeadName.ToLower().Contains(s) ||
                    p.CompanyName.ToLower().Contains(s) ||
                    p.ProjectRequirement.ToLower().Contains(s) ||
                    p.ScopeOfWork.ToLower().Contains(s) ||
                    p.Description.ToLower().Contains(s) ||
                    p.Notes.ToLower().Contains(s)).ToList();
            }

            list = list.OrderByDescending(p => p.Id).ToList();

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var totalCount = list.Count;
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var pagedItems = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var result = new PaginatedResult<AppProposalResponseDto>
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Items = pagedItems
            };

            return Ok(AppApiResponse<PaginatedResult<AppProposalResponseDto>>.Ok(result, "Proposals retrieved successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving proposals in AppApi.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving proposals."));
        }
    }

    /// <summary>
    /// GET /api/v1/app/proposals/{idOrNumber}
    /// Retrieves a single proposal by numeric Id or code number (e.g. PRO-2001).
    /// </summary>
    [HttpGet("{idOrNumber}")]
    public async Task<IActionResult> GetProposalById(string idOrNumber)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(idOrNumber))
            {
                return BadRequest(AppApiErrorResponse.Fail("Proposal identifier is required."));
            }

            var idTrim = idOrNumber.Trim();
            int.TryParse(idTrim, out var numericId);

            var leads = await LeadDataScope.ScopedLeadsQuery(_db, User).AsNoTracking().ToListAsync();
            foreach (var lead in leads)
            {
                if (lead.Proposals == null) continue;

                var pro = lead.Proposals.FirstOrDefault(p =>
                    (numericId > 0 && p.Id == numericId) ||
                    string.Equals(p.Number, idTrim, StringComparison.OrdinalIgnoreCase));

                if (pro != null)
                {
                    var dto = new AppProposalResponseDto
                    {
                        Id = pro.Id,
                        AdminId = pro.AdminId ?? lead.AdminId,
                        Number = pro.Number ?? "",
                        LeadId = lead.LeadId ?? "",
                        LeadName = lead.LeadName ?? "",
                        CompanyName = lead.CompanyName ?? "",
                        LeadCustomer = !string.IsNullOrWhiteSpace(pro.LeadCustomer)
                            ? pro.LeadCustomer
                            : $"{lead.LeadName} ({(string.IsNullOrWhiteSpace(lead.CompanyName) ? "Individual" : lead.CompanyName)})",
                        Title = !string.IsNullOrWhiteSpace(pro.Title) ? pro.Title : "Proposal",
                        Date = pro.Date ?? "",
                        ProjectRequirement = pro.ProjectRequirement ?? "",
                        ScopeOfWork = pro.ScopeOfWork ?? "",
                        Description = pro.Description ?? "",
                        Deliverables = pro.Deliverables ?? "",
                        Timeline = pro.Timeline ?? "",
                        Budget = pro.Budget ?? "",
                        Subtotal = pro.Subtotal ?? "0",
                        Discount = pro.Discount ?? "0",
                        TaxRate = pro.TaxRate ?? "0",
                        Tax = pro.Tax ?? "0",
                        TaxName = pro.TaxName ?? "GST",
                        TaxType = pro.TaxType ?? "CGST/SGST",
                        GrandTotal = pro.GrandTotal ?? "0",
                        TermsConditions = pro.TermsConditions ?? "",
                        Notes = pro.Notes ?? "",
                        Status = pro.Status ?? "Draft",
                        AttachmentPath = pro.AttachmentPath,
                        AttachmentFileName = pro.AttachmentFileName
                    };

                    return Ok(AppApiResponse<AppProposalResponseDto>.Ok(dto, "Proposal details retrieved successfully."));
                }
            }

            return NotFound(AppApiErrorResponse.Fail($"Proposal '{idOrNumber}' was not found or access is denied."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving proposal {IdOrNumber} in AppApi.", idOrNumber);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while retrieving proposal details."));
        }
    }

    /// <summary>
    /// POST /api/v1/app/proposals
    /// Creates a new Proposal against a lead.
    /// Automatically calculates subtotal, tax, discount, grand total, and assigns PRO-xxxx number.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateProposal([FromBody] AppCreateProposalRequest request)
    {
        try
        {
            if (request == null)
            {
                return BadRequest(AppApiErrorResponse.Fail("Request payload is required."));
            }

            if (string.IsNullOrWhiteSpace(request.LeadId))
            {
                return BadRequest(AppApiErrorResponse.Fail("LeadId is required to create a proposal."));
            }

            var leadIdTrim = request.LeadId.Trim();
            var lead = await LeadDataScope.ScopedLeadsQuery(_db, User)
                .Include(l => l.Proposals)
                .FirstOrDefaultAsync(l => l.LeadId == leadIdTrim || l.Id.ToString() == leadIdTrim);

            if (lead == null)
            {
                return NotFound(AppApiErrorResponse.Fail($"Lead '{request.LeadId}' was not found or access is denied."));
            }

            // Calculation
            var budgetVal = ParseDec(request.Budget);
            var subtotalVal = budgetVal > 0 ? budgetVal : ParseDec(request.Subtotal);
            var discountVal = ParseDec(request.Discount);
            var taxable = Math.Max(0m, subtotalVal - discountVal);
            var rateVal = ParseDec(request.TaxRate);
            var taxVal = rateVal > 0 ? (taxable * (rateVal / 100m)) : ParseDec(request.Tax);
            var grandTotal = taxable + taxVal;

            var number = await NextProposalNumberAsync();

            var entry = new ProposalEntry
            {
                Number = number,
                Date = !string.IsNullOrWhiteSpace(request.Date) ? request.Date.Trim() : DateTime.UtcNow.ToString("yyyy-MM-dd"),
                LeadCustomer = $"{lead.LeadName} ({(string.IsNullOrWhiteSpace(lead.CompanyName) ? "Individual" : lead.CompanyName)})",
                ProjectRequirement = request.ProjectRequirement ?? "",
                Title = !string.IsNullOrWhiteSpace(request.Title) ? request.Title.Trim() : "Proposal",
                ScopeOfWork = request.ScopeOfWork ?? "",
                Description = request.Description ?? (request.Notes ?? ""),
                Deliverables = request.Deliverables ?? "",
                Timeline = request.Timeline ?? "",
                Budget = request.Budget ?? subtotalVal.ToString("0.##"),
                Subtotal = subtotalVal.ToString("0.##"),
                Discount = discountVal.ToString("0.##"),
                TaxRate = rateVal.ToString("0.##"),
                Tax = taxVal.ToString("0.##"),
                TaxName = !string.IsNullOrWhiteSpace(request.TaxName) ? request.TaxName : "GST",
                TaxType = !string.IsNullOrWhiteSpace(request.TaxType) ? request.TaxType : "CGST/SGST",
                GrandTotal = grandTotal.ToString("0.##"),
                TermsConditions = request.TermsConditions ?? "",
                Notes = request.Notes ?? "",
                Status = !string.IsNullOrWhiteSpace(request.Status) ? request.Status : "Draft",
                AdminId = lead.AdminId
            };

            lead.Proposals.Add(entry);
            await _db.SaveChangesAsync();

            var dto = new AppProposalResponseDto
            {
                Id = entry.Id,
                AdminId = entry.AdminId,
                Number = entry.Number,
                LeadId = lead.LeadId ?? "",
                LeadName = lead.LeadName ?? "",
                CompanyName = lead.CompanyName ?? "",
                LeadCustomer = entry.LeadCustomer,
                Title = entry.Title,
                Date = entry.Date ?? "",
                ProjectRequirement = entry.ProjectRequirement ?? "",
                ScopeOfWork = entry.ScopeOfWork ?? "",
                Description = entry.Description ?? "",
                Deliverables = entry.Deliverables ?? "",
                Timeline = entry.Timeline ?? "",
                Budget = entry.Budget ?? "",
                Subtotal = entry.Subtotal ?? "0",
                Discount = entry.Discount ?? "0",
                TaxRate = entry.TaxRate ?? "0",
                Tax = entry.Tax ?? "0",
                TaxName = entry.TaxName ?? "",
                TaxType = entry.TaxType ?? "",
                GrandTotal = entry.GrandTotal ?? "0",
                TermsConditions = entry.TermsConditions ?? "",
                Notes = entry.Notes ?? "",
                Status = entry.Status ?? "Draft"
            };

            return Created($"/api/v1/app/proposals/{entry.Number}",
                AppApiResponse<AppProposalResponseDto>.Ok(dto, "Proposal created successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating proposal in AppApi.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                AppApiErrorResponse.Fail("An error occurred while creating the proposal."));
        }
    }
}

