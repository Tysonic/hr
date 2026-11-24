using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyHr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Onboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnboardingTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Owner = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AssigneeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnboardingTasks_Employees_AssigneeId",
                        column: x => x.AssigneeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OnboardingTasks_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OnboardingTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Items = table.Column<string>(type: "jsonb", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProbationReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProbationEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Recommendation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RecommendedExtendTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Comments = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    RecommendedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecommendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExtendedTo = table.Column<DateOnly>(type: "date", nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProbationReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProbationReviews_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProbationReviews_EmploymentContracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "EmploymentContracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingTasks_AssigneeId",
                table: "OnboardingTasks",
                column: "AssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingTasks_EmployeeId",
                table: "OnboardingTasks",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingTasks_Tenant",
                table: "OnboardingTasks",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingTasks_Tenant_AssigneeId",
                table: "OnboardingTasks",
                columns: new[] { "Tenant", "AssigneeId" });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingTasks_Tenant_EmployeeId",
                table: "OnboardingTasks",
                columns: new[] { "Tenant", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingTemplates_Tenant",
                table: "OnboardingTemplates",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingTemplates_Tenant_Name",
                table: "OnboardingTemplates",
                columns: new[] { "Tenant", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProbationReviews_ContractId",
                table: "ProbationReviews",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ProbationReviews_EmployeeId",
                table: "ProbationReviews",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProbationReviews_Tenant",
                table: "ProbationReviews",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_ProbationReviews_Tenant_ContractId",
                table: "ProbationReviews",
                columns: new[] { "Tenant", "ContractId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnboardingTasks");

            migrationBuilder.DropTable(
                name: "OnboardingTemplates");

            migrationBuilder.DropTable(
                name: "ProbationReviews");
        }
    }
}
