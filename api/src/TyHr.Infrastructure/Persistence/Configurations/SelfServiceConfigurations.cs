using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TyHr.Domain.Employees;
using TyHr.Domain.SelfService;

namespace TyHr.Infrastructure.Persistence.Configurations;

public class ProfileChangeRequestConfiguration : IEntityTypeConfiguration<ProfileChangeRequest>
{
    public void Configure(EntityTypeBuilder<ProfileChangeRequest> b)
    {
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.DecisionNote).HasMaxLength(1000);
        b.Property(x => x.RequestedBy).HasMaxLength(200);
        b.Property(x => x.DecidedBy).HasMaxLength(200);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.Status, x.Kind });
        b.HasIndex(x => new { x.Tenant, x.EmployeeId });
    }
}

public class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> b)
    {
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Body).HasMaxLength(10_000);
        b.Property(x => x.PublishedBy).HasMaxLength(200);
        b.HasIndex(x => new { x.Tenant, x.ExpiresOn });
    }
}
