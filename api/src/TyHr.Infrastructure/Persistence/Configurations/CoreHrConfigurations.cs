using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TyHr.Domain.Employees;
using TyHr.Domain.Organization;

namespace TyHr.Infrastructure.Persistence.Configurations;

// Names and codes are unique within a company (indexes lead with Tenant). References between company rows are real
// foreign keys with Restrict, so nothing in use can be deleted: deactivate it instead.

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Code).HasMaxLength(20);
        b.HasIndex(x => new { x.Tenant, x.Name }).IsUnique();
        b.HasIndex(x => new { x.Tenant, x.Code }).IsUnique();
    }
}

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Code).HasMaxLength(20);
        b.HasIndex(x => new { x.Tenant, x.Name }).IsUnique();
        b.HasIndex(x => new { x.Tenant, x.Code }).IsUnique();
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.HeadEmployeeId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class JobGradeConfiguration : IEntityTypeConfiguration<JobGrade>
{
    public void Configure(EntityTypeBuilder<JobGrade> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(100);
        b.HasIndex(x => new { x.Tenant, x.Code }).IsUnique();
    }
}

public class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(100);
        b.HasIndex(x => new { x.Tenant, x.DepartmentId, x.Title }).IsUnique();
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<JobGrade>().WithMany().HasForeignKey(x => x.JobGradeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.EmployeeNumber).HasMaxLength(20);
        b.HasIndex(x => new { x.Tenant, x.EmployeeNumber }).IsUnique();
        foreach (var name in new[] { nameof(Employee.FirstName), nameof(Employee.MiddleName), nameof(Employee.LastName) })
            b.Property(name).HasMaxLength(100);
        b.Property(x => x.Gender).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.MaritalStatus).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.IdType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Nationality).HasMaxLength(2);
        b.Property(x => x.TaxNumber).HasMaxLength(30);
        b.Property(x => x.SocialSecurityNumber).HasMaxLength(30);
        b.Property(x => x.PersonalEmail).HasMaxLength(254);
        b.Property(x => x.WorkEmail).HasMaxLength(254);
        b.Property(x => x.PersonalPhone).HasMaxLength(20);
        b.Property(x => x.WorkPhone).HasMaxLength(30);
        b.Property(x => x.BankName).HasMaxLength(100);
        b.Property(x => x.BankBranch).HasMaxLength(100);
        b.Property(x => x.BankAccountName).HasMaxLength(200);
        b.Property(x => x.MobileMoneyProvider).HasMaxLength(50);
        b.Ignore(x => x.FullName);
        // A work email identifies one employee's sign-in within the company.
        b.HasIndex(x => new { x.Tenant, x.WorkEmail }).IsUnique().HasFilter("\"WorkEmail\" IS NOT NULL");
        b.HasIndex(x => new { x.Tenant, x.LastName, x.FirstName });
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Position>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class NextOfKinConfiguration : IEntityTypeConfiguration<NextOfKin>
{
    public void Configure(EntityTypeBuilder<NextOfKin> b)
    {
        b.ToTable("NextOfKin");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Relationship).HasMaxLength(50);
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(254);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class EmploymentContractConfiguration : IEntityTypeConfiguration<EmploymentContract>
{
    public void Configure(EntityTypeBuilder<EmploymentContract> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.Tenant, x.EndDate });
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SalaryChangeConfiguration : IEntityTypeConfiguration<SalaryChange>
{
    public void Configure(EntityTypeBuilder<SalaryChange> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.CurrencyCode).HasMaxLength(3);
        b.Property(x => x.Reason).HasMaxLength(200);
        b.Property(x => x.RecordedBy).HasMaxLength(100);
        // One change per employee per effective date, so "the salary on a day" is never ambiguous.
        b.HasIndex(x => new { x.EmployeeId, x.EffectiveDate }).IsUnique();
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
