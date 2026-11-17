using System.ComponentModel.DataAnnotations;

namespace Crm.Api.Models;

public sealed record CustomerSummaryDto(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    DateOnly? SignupDate,
    string? City,
    string? AcquisitionChannel,
    string Status,
    bool IsSynthetic,
    Guid? OwnerUserId,
    string? ExternalId,
    DateTime CreatedAtUtc);

public sealed class CustomerWriteRequest
{
    [Required, StringLength(160, MinimumLength = 2)]
    public string Name { get; init; } = "";
    [EmailAddress, StringLength(256)]
    public string? Email { get; init; }
    [StringLength(40)]
    public string? Phone { get; init; }
    public DateOnly? SignupDate { get; init; }
    [StringLength(100)]
    public string? City { get; init; }
    [StringLength(60)]
    public string? AcquisitionChannel { get; init; }
    [RegularExpression("^(Active|Inactive|At Risk|active|inactive|at risk)$")]
    public string? Status { get; init; }
    [StringLength(100)]
    public string? ExternalId { get; init; }
    public Guid? OwnerUserId { get; init; }
}
