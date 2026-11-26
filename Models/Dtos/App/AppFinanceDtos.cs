using System.Text.Json.Serialization;

namespace CrmLeadManagement.Models.Dtos.App;

/// <summary>
/// Line item representation for Estimations and Quotations.
/// </summary>
public class AppFinanceLineItemDto
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("itemName")]
    public string? ItemName { get; set; } = "";

    [JsonPropertyName("quantity")]
    public string? Quantity { get; set; } = "1";

    [JsonPropertyName("rate")]
    public string? Rate { get; set; } = "0";

    [JsonPropertyName("amount")]
    public string? Amount { get; set; } = "0";
}

// =======================================================================
// 1. ESTIMATION DTOs
// =======================================================================

/// <summary>
/// Request payload for creating an Estimation via POST /api/v1/app/estimations.
/// </summary>
public class AppCreateEstimationRequest
{
    /// <summary>Lead identifier (e.g. "LD-1001" or numeric string "1"). Required.</summary>
    [JsonPropertyName("leadId")]
    public string LeadId { get; set; } = "";

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("projectRequirement")]
    public string? ProjectRequirement { get; set; }

    [JsonPropertyName("items")]
    public List<AppFinanceLineItemDto>? Items { get; set; } = new();

    [JsonPropertyName("discount")]
    public string? Discount { get; set; } = "0";

    [JsonPropertyName("taxRate")]
    public string? TaxRate { get; set; } = "0";

    [JsonPropertyName("tax")]
    public string? Tax { get; set; } = "0";

    [JsonPropertyName("taxName")]
    public string? TaxName { get; set; } = "GST";

    [JsonPropertyName("taxType")]
    public string? TaxType { get; set; } = "CGST/SGST";

    [JsonPropertyName("validUntil")]
    public string? ValidUntil { get; set; }

    [JsonPropertyName("termsConditions")]
    public string? TermsConditions { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; } = "Draft";
}

/// <summary>
/// Response payload for an Estimation.
/// </summary>
public class AppEstimationResponseDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("adminId")]
    public int? AdminId { get; set; }

    [JsonPropertyName("number")]
    public string Number { get; set; } = "";

    [JsonPropertyName("leadId")]
    public string LeadId { get; set; } = "";

    [JsonPropertyName("leadName")]
    public string LeadName { get; set; } = "";

    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = "";

    [JsonPropertyName("leadCustomer")]
    public string LeadCustomer { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("projectRequirement")]
    public string ProjectRequirement { get; set; } = "";

    [JsonPropertyName("items")]
    public List<AppFinanceLineItemDto> Items { get; set; } = new();

    [JsonPropertyName("subtotal")]
    public string Subtotal { get; set; } = "0";

    [JsonPropertyName("discount")]
    public string Discount { get; set; } = "0";

    [JsonPropertyName("taxRate")]
    public string TaxRate { get; set; } = "0";

    [JsonPropertyName("tax")]
    public string Tax { get; set; } = "0";

    [JsonPropertyName("taxName")]
    public string TaxName { get; set; } = "";

    [JsonPropertyName("taxType")]
    public string TaxType { get; set; } = "";

    [JsonPropertyName("total")]
    public string Total { get; set; } = "0";

    [JsonPropertyName("validUntil")]
    public string ValidUntil { get; set; } = "";

    [JsonPropertyName("termsConditions")]
    public string TermsConditions { get; set; } = "";

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Draft";

    [JsonPropertyName("attachmentPath")]
    public string? AttachmentPath { get; set; }

    [JsonPropertyName("attachmentFileName")]
    public string? AttachmentFileName { get; set; }
}

// =======================================================================
// 2. QUOTATION DTOs
// =======================================================================

/// <summary>
/// Request payload for creating a Quotation via POST /api/v1/app/quotations.
/// </summary>
public class AppCreateQuotationRequest
{
    /// <summary>Lead identifier (e.g. "LD-1001" or numeric string "1"). Required.</summary>
    [JsonPropertyName("leadId")]
    public string LeadId { get; set; } = "";

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("projectRequirement")]
    public string? ProjectRequirement { get; set; }

    [JsonPropertyName("items")]
    public List<AppFinanceLineItemDto>? Items { get; set; } = new();

    [JsonPropertyName("discount")]
    public string? Discount { get; set; } = "0";

    [JsonPropertyName("taxRate")]
    public string? TaxRate { get; set; } = "0";

    [JsonPropertyName("tax")]
    public string? Tax { get; set; } = "0";

    [JsonPropertyName("taxName")]
    public string? TaxName { get; set; } = "GST";

    [JsonPropertyName("taxType")]
    public string? TaxType { get; set; } = "CGST/SGST";

    [JsonPropertyName("validUntil")]
    public string? ValidUntil { get; set; }

    [JsonPropertyName("termsConditions")]
    public string? TermsConditions { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; } = "Draft";
}

/// <summary>
/// Response payload for a Quotation.
/// </summary>
public class AppQuotationResponseDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("adminId")]
    public int? AdminId { get; set; }

    [JsonPropertyName("number")]
    public string Number { get; set; } = "";

    [JsonPropertyName("leadId")]
    public string LeadId { get; set; } = "";

    [JsonPropertyName("leadName")]
    public string LeadName { get; set; } = "";

    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = "";

    [JsonPropertyName("leadCustomer")]
    public string LeadCustomer { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("projectRequirement")]
    public string ProjectRequirement { get; set; } = "";

    [JsonPropertyName("items")]
    public List<AppFinanceLineItemDto> Items { get; set; } = new();

    [JsonPropertyName("subtotal")]
    public string Subtotal { get; set; } = "0";

    [JsonPropertyName("discount")]
    public string Discount { get; set; } = "0";

    [JsonPropertyName("taxRate")]
    public string TaxRate { get; set; } = "0";

    [JsonPropertyName("tax")]
    public string Tax { get; set; } = "0";

    [JsonPropertyName("taxName")]
    public string TaxName { get; set; } = "";

    [JsonPropertyName("taxType")]
    public string TaxType { get; set; } = "";

    [JsonPropertyName("grandTotal")]
    public string GrandTotal { get; set; } = "0";

    [JsonPropertyName("validUntil")]
    public string ValidUntil { get; set; } = "";

    [JsonPropertyName("termsConditions")]
    public string TermsConditions { get; set; } = "";

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Draft";

    [JsonPropertyName("attachmentPath")]
    public string? AttachmentPath { get; set; }

    [JsonPropertyName("attachmentFileName")]
    public string? AttachmentFileName { get; set; }
}

// =======================================================================
// 3. PROPOSAL DTOs
// =======================================================================

/// <summary>
/// Request payload for creating a Proposal via POST /api/v1/app/proposals.
/// </summary>
public class AppCreateProposalRequest
{
    /// <summary>Lead identifier (e.g. "LD-1001" or numeric string "1"). Required.</summary>
    [JsonPropertyName("leadId")]
    public string LeadId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("projectRequirement")]
    public string? ProjectRequirement { get; set; }

    [JsonPropertyName("scopeOfWork")]
    public string? ScopeOfWork { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("deliverables")]
    public string? Deliverables { get; set; }

    [JsonPropertyName("timeline")]
    public string? Timeline { get; set; }

    [JsonPropertyName("budget")]
    public string? Budget { get; set; }

    [JsonPropertyName("subtotal")]
    public string? Subtotal { get; set; } = "0";

    [JsonPropertyName("discount")]
    public string? Discount { get; set; } = "0";

    [JsonPropertyName("taxRate")]
    public string? TaxRate { get; set; } = "0";

    [JsonPropertyName("tax")]
    public string? Tax { get; set; } = "0";

    [JsonPropertyName("taxName")]
    public string? TaxName { get; set; } = "GST";

    [JsonPropertyName("taxType")]
    public string? TaxType { get; set; } = "CGST/SGST";

    [JsonPropertyName("grandTotal")]
    public string? GrandTotal { get; set; } = "0";

    [JsonPropertyName("termsConditions")]
    public string? TermsConditions { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; } = "Draft";
}

/// <summary>
/// Response payload for a Proposal.
/// </summary>
public class AppProposalResponseDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("adminId")]
    public int? AdminId { get; set; }

    [JsonPropertyName("number")]
    public string Number { get; set; } = "";

    [JsonPropertyName("leadId")]
    public string LeadId { get; set; } = "";

    [JsonPropertyName("leadName")]
    public string LeadName { get; set; } = "";

    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = "";

    [JsonPropertyName("leadCustomer")]
    public string LeadCustomer { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("projectRequirement")]
    public string ProjectRequirement { get; set; } = "";

    [JsonPropertyName("scopeOfWork")]
    public string ScopeOfWork { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("deliverables")]
    public string Deliverables { get; set; } = "";

    [JsonPropertyName("timeline")]
    public string Timeline { get; set; } = "";

    [JsonPropertyName("budget")]
    public string Budget { get; set; } = "";

    [JsonPropertyName("subtotal")]
    public string Subtotal { get; set; } = "0";

    [JsonPropertyName("discount")]
    public string Discount { get; set; } = "0";

    [JsonPropertyName("taxRate")]
    public string TaxRate { get; set; } = "0";

    [JsonPropertyName("tax")]
    public string Tax { get; set; } = "0";

    [JsonPropertyName("taxName")]
    public string TaxName { get; set; } = "";

    [JsonPropertyName("taxType")]
    public string TaxType { get; set; } = "";

    [JsonPropertyName("grandTotal")]
    public string GrandTotal { get; set; } = "0";

    [JsonPropertyName("termsConditions")]
    public string TermsConditions { get; set; } = "";

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Draft";

    [JsonPropertyName("attachmentPath")]
    public string? AttachmentPath { get; set; }

    [JsonPropertyName("attachmentFileName")]
    public string? AttachmentFileName { get; set; }
}

