using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TyHr.Domain.Auditing;
using TyHr.Domain.Tenants;

namespace TyHr.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.HasKey(t => t.Id);
        b.Property(t => t.Slug).HasMaxLength(63);
        b.HasIndex(t => t.Slug).IsUnique();
        b.Property(t => t.Name).HasMaxLength(200);
        b.Property(t => t.CountryCode).HasMaxLength(2);
        b.Property(t => t.AdminEmail).HasMaxLength(254);
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.CreatedBy).HasMaxLength(100);
        b.Ignore(t => t.Country);
    }
}

public class CompanyProfileConfiguration : IEntityTypeConfiguration<CompanyProfile>
{
    public void Configure(EntityTypeBuilder<CompanyProfile> b)
    {
        b.HasKey(p => p.Id);
        // One profile per company.
        b.HasIndex(p => p.Tenant).IsUnique();
        b.Property(p => p.LegalName).HasMaxLength(200);
        b.Property(p => p.TradingName).HasMaxLength(200);
        b.Property(p => p.TaxNumber).HasMaxLength(30);
        b.Property(p => p.SocialSecurityNumber).HasMaxLength(30);
        b.Property(p => p.Email).HasMaxLength(254);
        b.Property(p => p.Phone).HasMaxLength(30);
        b.ToTable(t => t.HasCheckConstraint("CK_CompanyProfiles_FinancialYearStartMonth", "\"FinancialYearStartMonth\" BETWEEN 1 AND 12"));
    }
}

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).UseIdentityAlwaysColumn();
        b.Property(e => e.Tenant).HasMaxLength(63);
        b.Property(e => e.UserName).HasMaxLength(100);
        b.Property(e => e.EntityType).HasMaxLength(100);
        b.Property(e => e.EntityId).HasMaxLength(100);
        b.Property(e => e.Action).HasMaxLength(20);
        b.Property(e => e.Changes).HasColumnType("jsonb");
        b.HasIndex(e => new { e.Tenant, e.Id });
        b.HasIndex(e => new { e.Tenant, e.EntityType, e.EntityId });
    }
}

public class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    public void Configure(EntityTypeBuilder<DocumentSequence> b)
    {
        b.ToTable("Sequences");
        b.HasKey(s => new { s.Tenant, s.Prefix });
        b.Property(s => s.Tenant).HasMaxLength(63);
        b.Property(s => s.Prefix).HasMaxLength(20);
    }
}
