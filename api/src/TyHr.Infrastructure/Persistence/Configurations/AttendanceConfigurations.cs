using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TyHr.Domain.Attendance;
using TyHr.Domain.Employees;

namespace TyHr.Infrastructure.Persistence.Configurations;

public class WorkScheduleConfiguration : IEntityTypeConfiguration<WorkSchedule>
{
    public void Configure(EntityTypeBuilder<WorkSchedule> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);
        b.Ignore(x => x.Week);
        b.Ignore(x => x.Overnight);
        b.Ignore(x => x.ScheduledMinutes);
        b.HasIndex(x => new { x.Tenant, x.Name }).IsUnique();
    }
}

public class ScheduleAssignmentConfiguration : IEntityTypeConfiguration<ScheduleAssignment>
{
    public void Configure(EntityTypeBuilder<ScheduleAssignment> b)
    {
        b.Property(x => x.RecordedBy).HasMaxLength(200);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WorkSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.EmployeeId, x.EffectiveFrom }).IsUnique();
    }
}

public class AttendanceEnrollmentConfiguration : IEntityTypeConfiguration<AttendanceEnrollment>
{
    public void Configure(EntityTypeBuilder<AttendanceEnrollment> b)
    {
        b.Property(x => x.DeviceUserId).HasMaxLength(50);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.EmployeeId }).IsUnique();
        b.HasIndex(x => new { x.Tenant, x.DeviceUserId }).IsUnique();
    }
}

public class ClockEventConfiguration : IEntityTypeConfiguration<ClockEvent>
{
    public void Configure(EntityTypeBuilder<ClockEvent> b)
    {
        b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.RecordedBy).HasMaxLength(200);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.EmployeeId, x.Time });
    }
}
