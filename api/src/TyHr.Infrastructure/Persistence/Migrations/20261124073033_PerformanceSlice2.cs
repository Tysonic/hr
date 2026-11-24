using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyHr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceSlice2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Calibrate",
                table: "ReviewCycles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PeerFeedback",
                table: "ReviewCycles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResultsReleasedAt",
                table: "ReviewCycles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalibratedBy",
                table: "Appraisals",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalibrationNote",
                table: "Appraisals",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalRating",
                table: "Appraisals",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImprovementPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppraisalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    Objectives = table.Column<string>(type: "jsonb", nullable: false),
                    Support = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EmployeeComment = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImprovementPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImprovementPlans_Appraisals_AppraisalId",
                        column: x => x.AppraisalId,
                        principalTable: "Appraisals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ImprovementPlans_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImprovementPlans_Employees_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PeerFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppraisalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    Strengths = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    Improvements = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeerFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PeerFeedback_Appraisals_AppraisalId",
                        column: x => x.AppraisalId,
                        principalTable: "Appraisals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PeerFeedback_Employees_ReviewerId",
                        column: x => x.ReviewerId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalaryReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Percents = table.Column<List<decimal>>(type: "numeric(5,2)[]", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryReviews_ReviewCycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "ReviewCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanCheckIns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    On = table.Column<DateOnly>(type: "date", nullable: false),
                    Progress = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    By = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanCheckIns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanCheckIns_ImprovementPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "ImprovementPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalaryRecommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: false),
                    CurrentSalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Percent = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    ProposedSalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Adjusted = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AppliedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryRecommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryRecommendations_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryRecommendations_SalaryReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "SalaryReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImprovementPlans_AppraisalId",
                table: "ImprovementPlans",
                column: "AppraisalId");

            migrationBuilder.CreateIndex(
                name: "IX_ImprovementPlans_EmployeeId",
                table: "ImprovementPlans",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ImprovementPlans_OwnerId",
                table: "ImprovementPlans",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ImprovementPlans_Tenant",
                table: "ImprovementPlans",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_ImprovementPlans_Tenant_EmployeeId",
                table: "ImprovementPlans",
                columns: new[] { "Tenant", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PeerFeedback_AppraisalId",
                table: "PeerFeedback",
                column: "AppraisalId");

            migrationBuilder.CreateIndex(
                name: "IX_PeerFeedback_ReviewerId",
                table: "PeerFeedback",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_PeerFeedback_Tenant",
                table: "PeerFeedback",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_PeerFeedback_Tenant_AppraisalId_ReviewerId",
                table: "PeerFeedback",
                columns: new[] { "Tenant", "AppraisalId", "ReviewerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeerFeedback_Tenant_ReviewerId",
                table: "PeerFeedback",
                columns: new[] { "Tenant", "ReviewerId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanCheckIns_PlanId",
                table: "PlanCheckIns",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanCheckIns_Tenant",
                table: "PlanCheckIns",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_PlanCheckIns_Tenant_PlanId",
                table: "PlanCheckIns",
                columns: new[] { "Tenant", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryRecommendations_EmployeeId",
                table: "SalaryRecommendations",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryRecommendations_ReviewId",
                table: "SalaryRecommendations",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryRecommendations_Tenant",
                table: "SalaryRecommendations",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryRecommendations_Tenant_ReviewId_EmployeeId",
                table: "SalaryRecommendations",
                columns: new[] { "Tenant", "ReviewId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalaryReviews_CycleId",
                table: "SalaryReviews",
                column: "CycleId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryReviews_Tenant",
                table: "SalaryReviews",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryReviews_Tenant_CycleId",
                table: "SalaryReviews",
                columns: new[] { "Tenant", "CycleId" },
                unique: true);
            // Reviews finished before calibration existed: their final rating is the manager's.
            migrationBuilder.Sql("""UPDATE "Appraisals" SET "FinalRating" = "OverallRating" WHERE "OverallRating" IS NOT NULL AND "FinalRating" IS NULL""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PeerFeedback");

            migrationBuilder.DropTable(
                name: "PlanCheckIns");

            migrationBuilder.DropTable(
                name: "SalaryRecommendations");

            migrationBuilder.DropTable(
                name: "ImprovementPlans");

            migrationBuilder.DropTable(
                name: "SalaryReviews");

            migrationBuilder.DropColumn(
                name: "Calibrate",
                table: "ReviewCycles");

            migrationBuilder.DropColumn(
                name: "PeerFeedback",
                table: "ReviewCycles");

            migrationBuilder.DropColumn(
                name: "ResultsReleasedAt",
                table: "ReviewCycles");

            migrationBuilder.DropColumn(
                name: "CalibratedBy",
                table: "Appraisals");

            migrationBuilder.DropColumn(
                name: "CalibrationNote",
                table: "Appraisals");

            migrationBuilder.DropColumn(
                name: "FinalRating",
                table: "Appraisals");
        }
    }
}
