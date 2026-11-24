using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyHr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Hiring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Interviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PanelIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    Criteria = table.Column<List<string>>(type: "text[]", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Interviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Interviews_JobApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "JobApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MonthlySalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ContractType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContractEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ProbationMonths = table.Column<int>(type: "integer", nullable: false),
                    RespondBy = table.Column<DateOnly>(type: "date", nullable: false),
                    ExtraTerms = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResponseNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobOffers_JobApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "JobApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfferLetterTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferLetterTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Scorecards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Scores = table.Column<string>(type: "jsonb", nullable: false),
                    Recommendation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Comments = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scorecards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scorecards_Interviews_InterviewId",
                        column: x => x.InterviewId,
                        principalTable: "Interviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Interviews_ApplicationId",
                table: "Interviews",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Interviews_PanelIds",
                table: "Interviews",
                column: "PanelIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Interviews_Tenant",
                table: "Interviews",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_Interviews_Tenant_ApplicationId",
                table: "Interviews",
                columns: new[] { "Tenant", "ApplicationId" });

            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_ApplicationId",
                table: "JobOffers",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_Tenant",
                table: "JobOffers",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_Tenant_ApplicationId",
                table: "JobOffers",
                columns: new[] { "Tenant", "ApplicationId" });

            migrationBuilder.CreateIndex(
                name: "IX_OfferLetterTemplates_Tenant",
                table: "OfferLetterTemplates",
                column: "Tenant",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Scorecards_InterviewId",
                table: "Scorecards",
                column: "InterviewId");

            migrationBuilder.CreateIndex(
                name: "IX_Scorecards_Tenant",
                table: "Scorecards",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_Scorecards_Tenant_InterviewId_InterviewerId",
                table: "Scorecards",
                columns: new[] { "Tenant", "InterviewId", "InterviewerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobOffers");

            migrationBuilder.DropTable(
                name: "OfferLetterTemplates");

            migrationBuilder.DropTable(
                name: "Scorecards");

            migrationBuilder.DropTable(
                name: "Interviews");
        }
    }
}
