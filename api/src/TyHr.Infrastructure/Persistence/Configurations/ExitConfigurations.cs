using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TyHr.Domain.Employees;
using TyHr.Domain.Exits;

namespace TyHr.Infrastructure.Persistence.Configurations;

public class EmployeeExitConfiguration : IEntityTypeConfiguration<EmployeeExit>
{
    public void Configure(EntityTypeBuilder<EmployeeExit> b)
    {
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).HasMaxLength(3000);
        b.Property(x => x.CompletionNote).HasMaxLength(2000);
        b.Property(x => x.WithdrawalNote).HasMaxLength(2000);
        b.Property(x => x.RequestedBy).HasMaxLength(200);
        b.Property(x => x.AcceptedBy).HasMaxLength(200);
        b.Property(x => x.CompletedBy).HasMaxLength(200);
        b.Property(x => x.WithdrawnBy).HasMaxLength(200);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.EmployeeId });
        // At most one open exit per employee.
        b.HasIndex(x => new { x.Tenant, x.EmployeeId }).IsUnique().HasFilter("\"Status\" IN ('Requested', 'InProgress')").HasDatabaseName("IX_EmployeeExits_Open");
    }
}

public class ClearanceItemConfiguration : IEntityTypeConfiguration<ClearanceItem>
{
    public void Configure(EntityTypeBuilder<ClearanceItem> b)
    {
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Owner).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.CompletedBy).HasMaxLength(200);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasOne<EmployeeExit>().WithMany().HasForeignKey(x => x.ExitId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.Tenant, x.ExitId });
        b.HasIndex(x => new { x.Tenant, x.AssigneeId });
    }
}

public class ExitInterviewConfiguration : IEntityTypeConfiguration<ExitInterview>
{
    public void Configure(EntityTypeBuilder<ExitInterview> b)
    {
        b.Property(x => x.MainReason).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.WentWell).HasMaxLength(3000);
        b.Property(x => x.CouldImprove).HasMaxLength(3000);
        b.Property(x => x.Comments).HasMaxLength(3000);
        b.Property(x => x.SubmittedBy).HasMaxLength(200);
        b.HasOne<EmployeeExit>().WithMany().HasForeignKey(x => x.ExitId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.Tenant, x.ExitId }).IsUnique();
    }
}

public class FinalSettlementConfiguration : IEntityTypeConfiguration<FinalSettlement>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<FinalSettlement> b)
    {
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PreparedBy).HasMaxLength(200);
        b.Property(x => x.Lines).HasColumnType("jsonb").HasConversion(
            v => System.Text.Json.JsonSerializer.Serialize(v, Json),
            v => System.Text.Json.JsonSerializer.Deserialize<List<SettlementLine>>(v, Json) ?? new List<SettlementLine>(),
            new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<SettlementLine>>((a, c) => a!.SequenceEqual(c!),
                v => v.Aggregate(0, (h, l) => HashCode.Combine(h, l.GetHashCode())), v => v.ToList()));
        b.HasOne<EmployeeExit>().WithMany().HasForeignKey(x => x.ExitId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.Tenant, x.ExitId }).IsUnique();
        b.HasIndex(x => new { x.Tenant, x.RunId });
    }
}
