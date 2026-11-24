using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TyHr.Domain.Documents;
using TyHr.Domain.Employees;
using TyHr.Domain.Leave;

namespace TyHr.Infrastructure.Persistence.Configurations;

public class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> b)
    {
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Accrual).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.EligibleGender).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.DaysPerYear).HasPrecision(6, 2);
        b.Property(x => x.MaxDaysPerRequest).HasPrecision(6, 2);
        b.Property(x => x.CarryForwardMax).HasPrecision(6, 2);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasIndex(x => new { x.Tenant, x.Code }).IsUnique();
    }
}

public class PublicHolidayConfiguration : IEntityTypeConfiguration<PublicHoliday>
{
    public void Configure(EntityTypeBuilder<PublicHoliday> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);
        b.HasIndex(x => new { x.Tenant, x.Date }).IsUnique();
    }
}

public class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> b)
    {
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Days).HasPrecision(6, 2);
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.DecisionNote).HasMaxLength(1000);
        b.Property(x => x.RequestedBy).HasMaxLength(200);
        b.Property(x => x.DecidedBy).HasMaxLength(200);
        b.HasIndex(x => new { x.Tenant, x.EmployeeId, x.StartDate });
        b.HasIndex(x => new { x.Tenant, x.Status, x.StartDate });
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EmployeeDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class LeaveAdjustmentConfiguration : IEntityTypeConfiguration<LeaveAdjustment>
{
    public void Configure(EntityTypeBuilder<LeaveAdjustment> b)
    {
        b.Property(x => x.Days).HasPrecision(6, 2);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.RecordedBy).HasMaxLength(200);
        b.HasIndex(x => new { x.Tenant, x.EmployeeId, x.LeaveTypeId, x.LeaveYear });
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
