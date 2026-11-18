using Crm.Api.Models;
using Crm.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Data;

public sealed class CrmDbContext(
    DbContextOptions<CrmDbContext> options,
    ITenantContext tenantContext)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<CustomerTransaction> Transactions => Set<CustomerTransaction>();
    public DbSet<CustomerInteraction> Interactions => Set<CustomerInteraction>();
    public DbSet<CustomerFollowUp> FollowUps => Set<CustomerFollowUp>();
    public DbSet<CustomerNote> CustomerNotes => Set<CustomerNote>();
    public DbSet<CustomerActivitySnapshot> ActivitySnapshots => Set<CustomerActivitySnapshot>();
    public DbSet<CustomerOutcome> CustomerOutcomes => Set<CustomerOutcome>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Organization>().Property(row => row.Name).HasMaxLength(160).IsRequired();
        modelBuilder.Entity<ApplicationUser>()
            .HasOne<Organization>()
            .WithMany()
            .HasForeignKey(row => row.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ApplicationUser>().Property(row => row.DisplayName).HasMaxLength(120).IsRequired();
        modelBuilder.Entity<ApplicationUser>().HasIndex(row => row.OrganizationId);

        modelBuilder.Entity<Customer>().Property(row => row.Name).HasMaxLength(160).IsRequired();
        modelBuilder.Entity<Customer>().Property(row => row.Email).HasMaxLength(256);
        modelBuilder.Entity<Customer>().Property(row => row.Phone).HasMaxLength(40);
        modelBuilder.Entity<Customer>().Property(row => row.City).HasMaxLength(100);
        modelBuilder.Entity<Customer>().Property(row => row.AcquisitionChannel).HasMaxLength(60);
        modelBuilder.Entity<Customer>().Property(row => row.Status).HasMaxLength(30).IsRequired();
        modelBuilder.Entity<Customer>().Property(row => row.ExternalId).HasMaxLength(100);
        modelBuilder.Entity<Customer>().HasIndex(row => new { row.OrganizationId, row.Email }).IsUnique();
        modelBuilder.Entity<Customer>().HasIndex(row => new { row.OrganizationId, row.ExternalId }).IsUnique();
        modelBuilder.Entity<Customer>().HasIndex(row => new { row.OrganizationId, row.OwnerUserId, row.Name });
        modelBuilder.Entity<Customer>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(row => row.OwnerUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Lead>().Property(row => row.Name).HasMaxLength(160).IsRequired();
        modelBuilder.Entity<Lead>().Property(row => row.Email).HasMaxLength(256);
        modelBuilder.Entity<Lead>().Property(row => row.Phone).HasMaxLength(40);
        modelBuilder.Entity<Lead>().Property(row => row.Company).HasMaxLength(160);
        modelBuilder.Entity<Lead>().Property(row => row.Source).HasMaxLength(60);
        modelBuilder.Entity<Lead>().Property(row => row.Status).HasMaxLength(30).IsRequired();
        modelBuilder.Entity<Lead>().HasIndex(row => new { row.OrganizationId, row.Status, row.CreatedAtUtc });
        modelBuilder.Entity<Lead>().HasIndex(row => new { row.OrganizationId, row.Email }).IsUnique();
        modelBuilder.Entity<Lead>().HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(row => row.OwnerUserId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Lead>().HasOne<Customer>()
            .WithMany()
            .HasForeignKey(row => row.ConvertedCustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<CustomerTransaction>().Property(row => row.ExternalTransactionId).HasMaxLength(100).IsRequired();
        modelBuilder.Entity<CustomerTransaction>().Property(row => row.Amount).HasPrecision(12, 2);
        modelBuilder.Entity<CustomerTransaction>().Property(row => row.Category).HasMaxLength(80);
        modelBuilder.Entity<CustomerTransaction>().Property(row => row.Channel).HasMaxLength(40);
        modelBuilder.Entity<CustomerTransaction>()
            .HasIndex(row => new { row.OrganizationId, row.ExternalTransactionId }).IsUnique();
        modelBuilder.Entity<CustomerTransaction>()
            .HasIndex(row => new { row.OrganizationId, row.CustomerId, row.OrderDate });
        modelBuilder.Entity<CustomerTransaction>()
            .HasOne(row => row.Customer).WithMany(row => row.Transactions)
            .HasForeignKey(row => row.CustomerId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CustomerInteraction>().Property(row => row.Type).HasMaxLength(40).IsRequired();
        modelBuilder.Entity<CustomerInteraction>().Property(row => row.Summary).HasMaxLength(2000);
        modelBuilder.Entity<CustomerInteraction>().Property(row => row.Outcome).HasMaxLength(1000);
        modelBuilder.Entity<CustomerInteraction>()
            .HasIndex(row => new { row.OrganizationId, row.CustomerId, row.OccurredAtUtc });
        modelBuilder.Entity<CustomerInteraction>()
            .HasOne(row => row.Customer).WithMany(row => row.Interactions)
            .HasForeignKey(row => row.CustomerId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CustomerFollowUp>().Property(row => row.Subject).HasMaxLength(240).IsRequired();
        modelBuilder.Entity<CustomerFollowUp>()
            .HasIndex(row => new { row.OrganizationId, row.CompletedAtUtc, row.DueAtUtc });
        modelBuilder.Entity<CustomerFollowUp>()
            .HasOne(row => row.Customer).WithMany(row => row.FollowUps)
            .HasForeignKey(row => row.CustomerId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CustomerNote>().Property(row => row.Body).HasMaxLength(8000).IsRequired();
        modelBuilder.Entity<CustomerNote>()
            .HasOne(row => row.Customer).WithMany(row => row.Notes)
            .HasForeignKey(row => row.CustomerId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CustomerActivitySnapshot>()
            .HasIndex(row => new { row.OrganizationId, row.CustomerId, row.SnapshotDate }).IsUnique();
        modelBuilder.Entity<CustomerActivitySnapshot>()
            .HasOne(row => row.Customer).WithMany(row => row.ActivitySnapshots)
            .HasForeignKey(row => row.CustomerId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CustomerOutcome>()
            .HasIndex(row => new { row.OrganizationId, row.CustomerId, row.SnapshotDate }).IsUnique();
        modelBuilder.Entity<CustomerOutcome>()
            .HasOne(row => row.Customer).WithMany(row => row.Outcomes)
            .HasForeignKey(row => row.CustomerId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AuditLog>().Property(row => row.Action).HasMaxLength(80).IsRequired();
        modelBuilder.Entity<AuditLog>().Property(row => row.EntityType).HasMaxLength(80).IsRequired();
        modelBuilder.Entity<AuditLog>().Property(row => row.EntityId).HasMaxLength(120);
        modelBuilder.Entity<AuditLog>().Property(row => row.DetailsJson).HasMaxLength(4000);
        modelBuilder.Entity<AuditLog>().HasIndex(row => new { row.OrganizationId, row.CreatedAtUtc });

        modelBuilder.Entity<Customer>().HasQueryFilter(row =>
            tenantContext.OrganizationId != null && row.OrganizationId == tenantContext.OrganizationId);
        modelBuilder.Entity<Lead>().HasQueryFilter(row =>
            tenantContext.OrganizationId != null && row.OrganizationId == tenantContext.OrganizationId);
        modelBuilder.Entity<CustomerTransaction>().HasQueryFilter(row =>
            tenantContext.OrganizationId != null && row.OrganizationId == tenantContext.OrganizationId);
        modelBuilder.Entity<CustomerInteraction>().HasQueryFilter(row =>
            tenantContext.OrganizationId != null && row.OrganizationId == tenantContext.OrganizationId);
        modelBuilder.Entity<CustomerFollowUp>().HasQueryFilter(row =>
            tenantContext.OrganizationId != null && row.OrganizationId == tenantContext.OrganizationId);
        modelBuilder.Entity<CustomerNote>().HasQueryFilter(row =>
            tenantContext.OrganizationId != null && row.OrganizationId == tenantContext.OrganizationId);
        modelBuilder.Entity<CustomerActivitySnapshot>().HasQueryFilter(row =>
            tenantContext.OrganizationId != null && row.OrganizationId == tenantContext.OrganizationId);
        modelBuilder.Entity<CustomerOutcome>().HasQueryFilter(row =>
            tenantContext.OrganizationId != null && row.OrganizationId == tenantContext.OrganizationId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(row =>
            tenantContext.OrganizationId != null && row.OrganizationId == tenantContext.OrganizationId);
    }
}
