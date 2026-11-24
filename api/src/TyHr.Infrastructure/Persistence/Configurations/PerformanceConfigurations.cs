using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TyHr.Domain.Employees;
using TyHr.Domain.Performance;

namespace TyHr.Infrastructure.Persistence.Configurations;

public class ReviewCycleConfiguration : IEntityTypeConfiguration<ReviewCycle>
{
    public void Configure(EntityTypeBuilder<ReviewCycle> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.CreatedBy).HasMaxLength(200);
        b.HasIndex(x => new { x.Tenant, x.Name }).IsUnique();
    }
}

public class AppraisalConfiguration : IEntityTypeConfiguration<Appraisal>
{
    public void Configure(EntityTypeBuilder<Appraisal> b)
    {
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.GoalsAgreedBy).HasMaxLength(200);
        b.Property(x => x.ManagerSubmittedBy).HasMaxLength(200);
        b.Property(x => x.SelfComment).HasMaxLength(5000);
        b.Property(x => x.ManagerComment).HasMaxLength(5000);
        b.Property(x => x.EmployeeComment).HasMaxLength(5000);
        b.Property(x => x.OverallRating).HasPrecision(3, 1);
        b.Property(x => x.FinalRating).HasPrecision(3, 1);
        b.Property(x => x.CalibrationNote).HasMaxLength(2000);
        b.Property(x => x.CalibratedBy).HasMaxLength(200);
        b.Ignore(x => x.GoalsAgreed);
        b.HasOne<ReviewCycle>().WithMany().HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.Tenant, x.CycleId, x.EmployeeId }).IsUnique();
        b.HasIndex(x => new { x.Tenant, x.ManagerId });
    }
}

public class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> b)
    {
        b.Property(x => x.Title).HasMaxLength(300);
        b.Property(x => x.Measure).HasMaxLength(1000);
        b.Property(x => x.SelfComment).HasMaxLength(2000);
        b.Property(x => x.ManagerComment).HasMaxLength(2000);
        b.HasOne<Appraisal>().WithMany().HasForeignKey(x => x.AppraisalId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.Tenant, x.AppraisalId });
    }
}

public class PeerFeedbackConfiguration : IEntityTypeConfiguration<PeerFeedback>
{
    public void Configure(EntityTypeBuilder<PeerFeedback> b)
    {
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RequestedBy).HasMaxLength(200);
        b.Property(x => x.Strengths).HasMaxLength(3000);
        b.Property(x => x.Improvements).HasMaxLength(3000);
        b.HasOne<Appraisal>().WithMany().HasForeignKey(x => x.AppraisalId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.AppraisalId, x.ReviewerId }).IsUnique();
        b.HasIndex(x => new { x.Tenant, x.ReviewerId });
    }
}

public class ImprovementPlanConfiguration : IEntityTypeConfiguration<ImprovementPlan>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<ImprovementPlan> b)
    {
        b.Property(x => x.Reason).HasMaxLength(5000);
        b.Property(x => x.Support).HasMaxLength(3000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.CreatedBy).HasMaxLength(200);
        b.Property(x => x.ClosedBy).HasMaxLength(200);
        b.Property(x => x.Outcome).HasMaxLength(5000);
        b.Property(x => x.EmployeeComment).HasMaxLength(3000);
        b.Property(x => x.Objectives).HasColumnType("jsonb").HasConversion(
            v => System.Text.Json.JsonSerializer.Serialize(v, Json),
            v => System.Text.Json.JsonSerializer.Deserialize<List<PlanObjective>>(v, Json) ?? new List<PlanObjective>(),
            new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<PlanObjective>>((a, c) => a!.SequenceEqual(c!),
                v => v.Aggregate(0, (h, o) => HashCode.Combine(h, o.GetHashCode())), v => v.ToList()));
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Appraisal>().WithMany().HasForeignKey(x => x.AppraisalId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.Tenant, x.EmployeeId });
    }
}

public class PlanCheckInConfiguration : IEntityTypeConfiguration<PlanCheckIn>
{
    public void Configure(EntityTypeBuilder<PlanCheckIn> b)
    {
        b.Property(x => x.Progress).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Note).HasMaxLength(5000);
        b.Property(x => x.By).HasMaxLength(200);
        b.HasOne<ImprovementPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.Tenant, x.PlanId });
    }
}

public class SalaryReviewConfiguration : IEntityTypeConfiguration<SalaryReview>
{
    public void Configure(EntityTypeBuilder<SalaryReview> b)
    {
        b.Property(x => x.Percents).HasColumnType("numeric(5,2)[]");
        b.HasOne<ReviewCycle>().WithMany().HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.Tenant, x.CycleId }).IsUnique();
    }
}

public class SalaryRecommendationConfiguration : IEntityTypeConfiguration<SalaryRecommendation>
{
    public void Configure(EntityTypeBuilder<SalaryRecommendation> b)
    {
        b.Property(x => x.Rating).HasPrecision(3, 1);
        b.Property(x => x.Percent).HasPrecision(7, 2);
        b.Property(x => x.CurrentSalary).HasPrecision(18, 2);
        b.Property(x => x.ProposedSalary).HasPrecision(18, 2);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.AppliedBy).HasMaxLength(200);
        b.HasOne<SalaryReview>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.ReviewId, x.EmployeeId }).IsUnique();
    }
}
