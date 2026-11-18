using System.ComponentModel.DataAnnotations;

namespace Crm.Api.Models;

public sealed record LeadSummaryDto(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    string? Company,
    string? Source,
    string Status,
    Guid? OwnerUserId,
    Guid? ConvertedCustomerId,
    DateTime CreatedAtUtc);

public sealed class TransactionWriteRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string ExternalTransactionId { get; init; } = "";
    public DateOnly OrderDate { get; init; }
    [Range(typeof(decimal), "0.01", "1000000000")]
    public decimal Amount { get; init; }
    [StringLength(80)]
    public string? Category { get; init; }
    [StringLength(40)]
    public string? Channel { get; init; }
}

public sealed class InteractionWriteRequest
{
    [Required, RegularExpression("(?i)^(call|meeting|email|support_case|enquiry|inquiry|other)$")]
    public string Type { get; init; } = "";
    [StringLength(2000)]
    public string? Summary { get; init; }
    [StringLength(1000)]
    public string? Outcome { get; init; }
    public DateTimeOffset? OccurredAtUtc { get; init; }
}

public sealed class FollowUpWriteRequest
{
    [Required, StringLength(240, MinimumLength = 2)]
    public string Subject { get; init; } = "";
    public DateTimeOffset DueAtUtc { get; init; }
    public Guid? OwnerUserId { get; init; }
}

public sealed class CustomerNoteWriteRequest
{
    [Required, StringLength(8000, MinimumLength = 1)]
    public string Body { get; init; } = "";
}

public sealed class LeadWriteRequest
{
    [Required, StringLength(160, MinimumLength = 2)]
    public string Name { get; init; } = "";
    [EmailAddress, StringLength(256)]
    public string? Email { get; init; }
    [StringLength(40)]
    public string? Phone { get; init; }
    [StringLength(160)]
    public string? Company { get; init; }
    [StringLength(60)]
    public string? Source { get; init; }
    public Guid? OwnerUserId { get; init; }
}

public sealed class LeadStatusRequest
{
    [Required]
    public string Status { get; init; } = "";
}
