using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TyHr.Domain.Employees;
using TyHr.Domain.Payroll;

namespace TyHr.Infrastructure.Persistence.Configurations;

public class PayComponentConfiguration : IEntityTypeConfiguration<PayComponent>
{
    public void Configure(EntityTypeBuilder<PayComponent> b)
    {
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasIndex(x => new { x.Tenant, x.Code }).IsUnique();
    }
}

public class EmployeePayItemConfiguration : IEntityTypeConfiguration<EmployeePayItem>
{
    public void Configure(EntityTypeBuilder<EmployeePayItem> b)
    {
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PayComponent>().WithMany().HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.EmployeeId });
    }
}

public class EmployeeTaxProfileConfiguration : IEntityTypeConfiguration<EmployeeTaxProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeTaxProfile> b)
    {
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.EmployeeId }).IsUnique();
    }
}

public class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> b)
    {
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.ReturnNote).HasMaxLength(1000);
        foreach (var name in new[] { nameof(PayrollRun.CreatedBy), nameof(PayrollRun.CalculatedBy), nameof(PayrollRun.SubmittedBy), nameof(PayrollRun.ApprovedBy), nameof(PayrollRun.LockedBy) })
            b.Property(name).HasMaxLength(200);
        foreach (var name in new[] { nameof(PayrollRun.Gross), nameof(PayrollRun.Deductions), nameof(PayrollRun.Net), nameof(PayrollRun.EmployerCost) })
            b.Property(name).HasPrecision(18, 2);
        b.Ignore(x => x.PeriodStart);
        b.Ignore(x => x.PeriodEnd);
        b.Ignore(x => x.IsFinal);
        b.Property(x => x.Version).IsRowVersion();
        b.HasOne<PayrollRun>().WithMany().HasForeignKey(x => x.ReversesRunId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.Year, x.Month });
        b.HasIndex(x => x.ReversesRunId).IsUnique();
    }
}

public class PayslipConfiguration : IEntityTypeConfiguration<Payslip>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Payslip> b)
    {
        b.Property(x => x.EmployeeNumber).HasMaxLength(20);
        b.Property(x => x.EmployeeName).HasMaxLength(200);
        b.Property(x => x.DepartmentName).HasMaxLength(100);
        b.Property(x => x.PositionTitle).HasMaxLength(100);
        b.Property(x => x.TaxNumber).HasMaxLength(50);
        b.Property(x => x.SocialSecurityNumber).HasMaxLength(50);
        b.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.BankName).HasMaxLength(100);
        b.Property(x => x.BankBranch).HasMaxLength(100);
        b.Property(x => x.BankAccountName).HasMaxLength(200);
        b.Property(x => x.MobileMoneyProvider).HasMaxLength(50);
        foreach (var name in new[] { nameof(Payslip.Gross), nameof(Payslip.TaxableGross), nameof(Payslip.PensionableGross), nameof(Payslip.TotalDeductions),
                     nameof(Payslip.Net), nameof(Payslip.EmployerContributions) })
            b.Property(name).HasPrecision(18, 2);
        b.Property(x => x.Lines).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, Json),
            v => JsonSerializer.Deserialize<List<PayLine>>(v, Json) ?? new List<PayLine>(),
            new ValueComparer<List<PayLine>>((a, c) => a!.SequenceEqual(c!), v => v.Aggregate(0, (h, l) => HashCode.Combine(h, l.GetHashCode())), v => v.ToList()));
        b.HasOne<PayrollRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.RunId, x.EmployeeId }).IsUnique();
        b.HasIndex(x => new { x.Tenant, x.EmployeeId });
    }
}

public class PayrollInputConfiguration : IEntityTypeConfiguration<PayrollInput>
{
    public void Configure(EntityTypeBuilder<PayrollInput> b)
    {
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasOne<PayrollRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PayComponent>().WithMany().HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.RunId });
    }
}
