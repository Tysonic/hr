using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TyHr.Domain.Organization;
using TyHr.Domain.Recruitment;

namespace TyHr.Infrastructure.Persistence.Configurations;

public class JobRequisitionConfiguration : IEntityTypeConfiguration<JobRequisition>
{
    public void Configure(EntityTypeBuilder<JobRequisition> b)
    {
        b.Property(x => x.Number).HasMaxLength(20);
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.EmploymentType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.SalaryMin).HasPrecision(18, 2);
        b.Property(x => x.SalaryMax).HasPrecision(18, 2);
        b.Property(x => x.Justification).HasMaxLength(4000);
        b.Property(x => x.RequestedBy).HasMaxLength(200);
        b.Property(x => x.DecidedBy).HasMaxLength(200);
        b.Property(x => x.DecisionNote).HasMaxLength(1000);
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Position>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.Number }).IsUnique();
        b.HasIndex(x => new { x.Tenant, x.Status });
    }
}

public class JobPostingConfiguration : IEntityTypeConfiguration<JobPosting>
{
    public void Configure(EntityTypeBuilder<JobPosting> b)
    {
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(20_000);
        b.Property(x => x.Location).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasOne<JobRequisition>().WithMany().HasForeignKey(x => x.RequisitionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.Status });
    }
}

public class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> b)
    {
        b.Property(x => x.FirstName).HasMaxLength(100);
        b.Property(x => x.LastName).HasMaxLength(100);
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.CoverLetter).HasMaxLength(10_000);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Stage).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.CvFileName).HasMaxLength(255);
        b.Property(x => x.CvContentType).HasMaxLength(100);
        b.Property(x => x.CvStorageKey).HasMaxLength(200);
        b.HasOne<JobPosting>().WithMany().HasForeignKey(x => x.PostingId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.PostingId, x.Email }).IsUnique();
    }
}

public class ApplicationEventConfiguration : IEntityTypeConfiguration<ApplicationEvent>
{
    public void Configure(EntityTypeBuilder<ApplicationEvent> b)
    {
        b.Property(x => x.Text).HasMaxLength(5_000);
        b.Property(x => x.Stage).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.By).HasMaxLength(200);
        b.HasOne<JobApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.Tenant, x.ApplicationId });
    }
}

public class InterviewConfiguration : IEntityTypeConfiguration<Interview>
{
    public void Configure(EntityTypeBuilder<Interview> b)
    {
        b.Property(x => x.Location).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.CreatedBy).HasMaxLength(200);
        b.HasOne<JobApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.ApplicationId });
        b.HasIndex(x => x.PanelIds).HasMethod("gin");
    }
}

public class ScorecardConfiguration : IEntityTypeConfiguration<Scorecard>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Scorecard> b)
    {
        b.Property(x => x.InterviewerName).HasMaxLength(200);
        b.Property(x => x.Recommendation).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Comments).HasMaxLength(5_000);
        b.Property(x => x.Scores).HasColumnType("jsonb").HasConversion(
            v => System.Text.Json.JsonSerializer.Serialize(v, Json),
            v => System.Text.Json.JsonSerializer.Deserialize<List<CriterionScore>>(v, Json) ?? new List<CriterionScore>(),
            new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<CriterionScore>>((a, c) => a!.SequenceEqual(c!),
                v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())), v => v.ToList()));
        b.Ignore(x => x.Average);
        b.HasOne<Interview>().WithMany().HasForeignKey(x => x.InterviewId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.Tenant, x.InterviewId, x.InterviewerId }).IsUnique();
    }
}

public class JobOfferConfiguration : IEntityTypeConfiguration<JobOffer>
{
    public void Configure(EntityTypeBuilder<JobOffer> b)
    {
        b.Property(x => x.JobTitle).HasMaxLength(200);
        b.Property(x => x.MonthlySalary).HasPrecision(18, 2);
        b.Property(x => x.ContractType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ExtraTerms).HasMaxLength(10_000);
        b.Property(x => x.CreatedBy).HasMaxLength(200);
        b.Property(x => x.ResponseNote).HasMaxLength(1000);
        b.Ignore(x => x.IsLive);
        b.HasOne<JobApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tenant, x.ApplicationId });
    }
}

public class OfferLetterTemplateConfiguration : IEntityTypeConfiguration<OfferLetterTemplate>
{
    public void Configure(EntityTypeBuilder<OfferLetterTemplate> b)
    {
        b.Property(x => x.Body).HasMaxLength(20_000);
        b.HasIndex(x => x.Tenant).IsUnique();
    }
}
