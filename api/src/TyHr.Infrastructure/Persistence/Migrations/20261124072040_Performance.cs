using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyHr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Performance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReviewCycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    SelfReview = table.Column<bool>(type: "boolean", nullable: false),
                    GoalsDue = table.Column<DateOnly>(type: "date", nullable: true),
                    ReviewsDue = table.Column<DateOnly>(type: "date", nullable: true),
                    Scale = table.Column<List<string>>(type: "text[]", nullable: false),
                    DepartmentIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LaunchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewCycles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Appraisals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GoalsSubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GoalsAgreedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GoalsAgreedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SelfComment = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    SelfSubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ManagerComment = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    ManagerSubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ManagerSubmittedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OverallRating = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EmployeeComment = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appraisals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Appraisals_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appraisals_Employees_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Appraisals_ReviewCycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "ReviewCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Goals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppraisalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Measure = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    SelfRating = table.Column<int>(type: "integer", nullable: true),
                    SelfComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ManagerRating = table.Column<int>(type: "integer", nullable: true),
                    ManagerComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Goals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Goals_Appraisals_AppraisalId",
                        column: x => x.AppraisalId,
                        principalTable: "Appraisals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appraisals_CycleId",
                table: "Appraisals",
                column: "CycleId");

            migrationBuilder.CreateIndex(
                name: "IX_Appraisals_EmployeeId",
                table: "Appraisals",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Appraisals_ManagerId",
                table: "Appraisals",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_Appraisals_Tenant",
                table: "Appraisals",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_Appraisals_Tenant_CycleId_EmployeeId",
                table: "Appraisals",
                columns: new[] { "Tenant", "CycleId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appraisals_Tenant_ManagerId",
                table: "Appraisals",
                columns: new[] { "Tenant", "ManagerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Goals_AppraisalId",
                table: "Goals",
                column: "AppraisalId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_Tenant",
                table: "Goals",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_Tenant_AppraisalId",
                table: "Goals",
                columns: new[] { "Tenant", "AppraisalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewCycles_Tenant",
                table: "ReviewCycles",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewCycles_Tenant_Name",
                table: "ReviewCycles",
                columns: new[] { "Tenant", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Goals");

            migrationBuilder.DropTable(
                name: "Appraisals");

            migrationBuilder.DropTable(
                name: "ReviewCycles");
        }
    }
}
