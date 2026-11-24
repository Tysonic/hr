using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TyHr.Domain.Documents;
using TyHr.Domain.Employees;
using TyHr.Domain.Reminders;

namespace TyHr.Infrastructure.Persistence.Configurations;

public class EmployeeDocumentConfiguration : IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeDocument> b)
    {
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.FileName).HasMaxLength(255);
        b.Property(x => x.ContentType).HasMaxLength(120);
        b.Property(x => x.StorageKey).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.UploadedBy).HasMaxLength(200);
        b.HasIndex(x => new { x.Tenant, x.EmployeeId });
        b.HasIndex(x => new { x.Tenant, x.ExpiresOn });
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ReminderLogConfiguration : IEntityTypeConfiguration<ReminderLog>
{
    public void Configure(EntityTypeBuilder<ReminderLog> b)
    {
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(x => new { x.Tenant, x.Kind, x.ItemId, x.DueOn, x.DaysBefore }).IsUnique();
    }
}
