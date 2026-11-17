namespace Crm.Api.Models;

public sealed class Customer : TenantEntity
{
    public required string Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateOnly? SignupDate { get; set; }
    public string? City { get; set; }
    public string? AcquisitionChannel { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsSynthetic { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string? ExternalId { get; set; }
    public ICollection<CustomerTransaction> Transactions { get; set; } = [];
    public ICollection<CustomerInteraction> Interactions { get; set; } = [];
    public ICollection<CustomerNote> Notes { get; set; } = [];
    public ICollection<CustomerFollowUp> FollowUps { get; set; } = [];
    public ICollection<CustomerActivitySnapshot> ActivitySnapshots { get; set; } = [];
    public ICollection<CustomerOutcome> Outcomes { get; set; } = [];
}
