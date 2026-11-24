using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TyHr.Domain.Employees;
using TyHr.Domain.Onboarding;

namespace TyHr.Infrastructure.Persistence.Configurations;

public class OnboardingTemplateConfiguration : IEntityTypeConfiguration<OnboardingTemplate>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public void Configure(EntityTypeBuilder<OnboardingTemplate> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Items).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, Json),
            v => JsonSerializer.Deserialize<List<ChecklistItem>>(v, Json) ?? new List<ChecklistItem>(),
            new ValueComparer<List<ChecklistItem>>((a, c) => a!.SequenceEqual(c!), v => v.Aggregate(0, (h, i) => HashCode.Combine(h, i.GetHashCode())), v => v.ToList()));
        b.HasIndex(x => new { x.Tenant, x.Name }).IsUnique();
    }
}

public class OnboardingTaskConfiguration : IEntityTypeConfiguration<OnboardingTask>
{
    public void Configure(EntityTypeBuilder<OnboardingTask> b)
    {
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Owner).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.CompletedBy).HasMaxLength(200);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Ignore(x => x.IsDone);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.Tenant, x.EmployeeId });
        b.HasIndex(x => new { x.Tenant, x.AssigneeId });
    }
}

public class ProbationReviewConfiguration : IEntityTypeConfiguration<ProbationReview>
{
    public void Configure(EntityTypeBuilder<ProbationReview> b)
    {
        b.Property(x => x.Recommendation).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Comments).HasMaxLength(5000);
        b.Property(x => x.RecommendedBy).HasMaxLength(200);
        b.Property(x => x.DecidedBy).HasMaxLength(200);
        b.Property(x => x.DecisionNote).HasMaxLength(2000);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<EmploymentContract>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.Tenant, x.ContractId });
    }
}
