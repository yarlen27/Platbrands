using Newtonsoft.Json;

namespace IngestaArchivosAPI.Models;

public class TransactionHeader
{
    [JsonProperty("id")]
    public int? Id { get; set; }

    [JsonProperty("timestamp")]
    public string? Timestamp { get; set; }

    [JsonProperty("period")]
    public string? Period { get; set; }

    [JsonProperty("date")]
    public string? Date { get; set; }

    [JsonProperty("insurance")]
    public string? Insurance { get; set; }

    [JsonProperty("amount")]
    public string? Amount { get; set; }

    [JsonProperty("checkNumber")]
    public string? CheckNumber { get; set; }

    [JsonProperty("isActive")]
    public bool IsActive { get; set; }

    [JsonProperty("postedByLsi")]
    public bool PostedByLsi { get; set; }

    [JsonProperty("verifyTotalAmount")]
    public bool VerifyTotalAmount { get; set; }

    [JsonProperty("claimNumber")]
    public int ClaimNumber { get; set; }

    [JsonProperty("problemReported")]
    public string? ProblemReported { get; set; }

    [JsonProperty("customColor")]
    public CustomColor? CustomColor { get; set; }

    [JsonProperty("eftAmountDifference")]
    public decimal EftAmountDifference { get; set; }

    [JsonProperty("checkAmountDifference")]
    public decimal CheckAmountDifference { get; set; }

    [JsonProperty("typeAdjusment")]
    public string? TypeAdjustment { get; set; }

    [JsonProperty("isDayEndedByCashPoster")]
    public bool IsDayEndedByCashPoster { get; set; }

    [JsonProperty("dayEndedByCashPosterTimestamp")]
    public string? DayEndedByCashPosterTimestamp { get; set; }

    [JsonProperty("creatorUserId")]
    public int CreatorUserId { get; set; }

    [JsonProperty("creatorUserName")]
    public string? CreatorUserName { get; set; }

    [JsonProperty("paymentTypeId")]
    public int PaymentTypeId { get; set; }

    [JsonProperty("paymentTypeName")]
    public string? PaymentTypeName { get; set; }

    [JsonProperty("officeId")]
    public int OfficeId { get; set; }

    [JsonProperty("officeName")]
    public string? OfficeName { get; set; }

    [JsonProperty("uploadClaimId")]
    public int UploadClaimId { get; set; }

    [JsonProperty("orderInList")]
    public int OrderInList { get; set; }

    [JsonProperty("details")]
    public List<TransactionDetail>? Details { get; set; }
}

public class TransactionDetail
{
    [JsonProperty("id")]
    public int? Id { get; set; }

    [JsonProperty("checkNumber")]
    public string? CheckNumber { get; set; }

    [JsonProperty("amount")]
    public decimal Amount { get; set; }

    [JsonProperty("patientName")]
    public string? PatientName { get; set; }

    [JsonProperty("cpdpId")]
    public int CpdpId { get; set; }

    [JsonProperty("flashReportNote")]
    public string? FlashReportNote { get; set; }

    [JsonProperty("flashReportTipology")]
    public string? FlashReportTipology { get; set; }

    [JsonProperty("tipologyServiceDate")]
    public string? TipologyServiceDate { get; set; }

    [JsonProperty("auditIsMandatory")]
    public bool AuditIsMandatory { get; set; }

    [JsonProperty("auditMaxDate")]
    public string? AuditMaxDate { get; set; }

    [JsonProperty("auditDone")]
    public bool AuditDone { get; set; }

    [JsonProperty("maxDatePenalty")]
    public bool MaxDatePenalty { get; set; }

    [JsonProperty("auditData")]
    public object? AuditData { get; set; }

    [JsonProperty("auditCustomColor")]
    public object? AuditCustomColor { get; set; }

    [JsonProperty("auditNote")]
    public string? AuditNote { get; set; }

    [JsonProperty("verify")]
    public bool? Verify { get; set; }

    [JsonProperty("verifyBy")]
    public string? VerifyBy { get; set; }

    [JsonProperty("verifyTimestamp")]
    public string? VerifyTimestamp { get; set; }

    [JsonProperty("modified")]
    public string? Modified { get; set; }

    [JsonProperty("subOrderInList")]
    public int SubOrderInList { get; set; }

    [JsonProperty("showRow")]
    public bool ShowRow { get; set; }

    [JsonProperty("removeDetail")]
    public bool RemoveDetail { get; set; }

    [JsonProperty("lastRow")]
    public bool LastRow { get; set; }
}

public class CustomColor
{
    [JsonProperty("background")]
    public string? Background { get; set; } = "#FFFFFF";

    [JsonProperty("textColor")]
    public string? TextColor { get; set; } = "#000000";
}


