using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyHr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Recruitment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobRequisitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Openings = table.Column<int>(type: "integer", nullable: false),
                    EmploymentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SalaryMin = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SalaryMax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    TargetStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Justification = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRequisitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobRequisitions_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobRequisitions_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobRequisitions_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobPostings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ClosingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ShowSalary = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobPostings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobPostings_JobRequisitions_RequisitionId",
                        column: x => x.RequisitionId,
                        principalTable: "JobRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostingId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CoverLetter = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CvFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CvContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CvSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CvStorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsentedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobApplications_JobPostings_PostingId",
                        column: x => x.PostingId,
                        principalTable: "JobPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    Stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    By = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationEvents_JobApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "JobApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvents_ApplicationId",
                table: "ApplicationEvents",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvents_Tenant",
                table: "ApplicationEvents",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvents_Tenant_ApplicationId",
                table: "ApplicationEvents",
                columns: new[] { "Tenant", "ApplicationId" });

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_PostingId",
                table: "JobApplications",
                column: "PostingId");

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_Tenant",
                table: "JobApplications",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_Tenant_PostingId_Email",
                table: "JobApplications",
                columns: new[] { "Tenant", "PostingId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobPostings_RequisitionId",
                table: "JobPostings",
                column: "RequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPostings_Tenant",
                table: "JobPostings",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_JobPostings_Tenant_Status",
                table: "JobPostings",
                columns: new[] { "Tenant", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_JobRequisitions_BranchId",
                table: "JobRequisitions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_JobRequisitions_DepartmentId",
                table: "JobRequisitions",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_JobRequisitions_PositionId",
                table: "JobRequisitions",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_JobRequisitions_Tenant",
                table: "JobRequisitions",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_JobRequisitions_Tenant_Number",
                table: "JobRequisitions",
                columns: new[] { "Tenant", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobRequisitions_Tenant_Status",
                table: "JobRequisitions",
                columns: new[] { "Tenant", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationEvents");

            migrationBuilder.DropTable(
                name: "JobApplications");

            migrationBuilder.DropTable(
                name: "JobPostings");

            migrationBuilder.DropTable(
                name: "JobRequisitions");
        }
    }
}
