using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyHr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Exits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeExits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    NoticeGivenOn = table.Column<DateOnly>(type: "date", nullable: false),
                    LastWorkingDay = table.Column<DateOnly>(type: "date", nullable: false),
                    PayInLieu = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EligibleForRehire = table.Column<bool>(type: "boolean", nullable: true),
                    CompletionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CompletedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WithdrawnBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WithdrawnAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WithdrawalNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeExits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeExits_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClearanceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExitId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Owner = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AssigneeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueDays = table.Column<int>(type: "integer", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearanceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClearanceItems_EmployeeExits_ExitId",
                        column: x => x.ExitId,
                        principalTable: "EmployeeExits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClearanceItems_Employees_AssigneeId",
                        column: x => x.AssigneeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ExitInterviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExitId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    MainReason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    RoleRating = table.Column<int>(type: "integer", nullable: true),
                    ManagerRating = table.Column<int>(type: "integer", nullable: true),
                    PayRating = table.Column<int>(type: "integer", nullable: true),
                    GrowthRating = table.Column<int>(type: "integer", nullable: true),
                    CultureRating = table.Column<int>(type: "integer", nullable: true),
                    WouldRecommend = table.Column<bool>(type: "boolean", nullable: true),
                    WouldReturn = table.Column<bool>(type: "boolean", nullable: true),
                    WentWell = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    CouldImprove = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    Comments = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    ByEmployee = table.Column<bool>(type: "boolean", nullable: false),
                    SubmittedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExitInterviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExitInterviews_EmployeeExits_ExitId",
                        column: x => x.ExitId,
                        principalTable: "EmployeeExits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceItems_AssigneeId",
                table: "ClearanceItems",
                column: "AssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceItems_ExitId",
                table: "ClearanceItems",
                column: "ExitId");

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceItems_Tenant",
                table: "ClearanceItems",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceItems_Tenant_AssigneeId",
                table: "ClearanceItems",
                columns: new[] { "Tenant", "AssigneeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceItems_Tenant_ExitId",
                table: "ClearanceItems",
                columns: new[] { "Tenant", "ExitId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeExits_EmployeeId",
                table: "EmployeeExits",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeExits_Open",
                table: "EmployeeExits",
                columns: new[] { "Tenant", "EmployeeId" },
                unique: true,
                filter: "\"Status\" IN ('Requested', 'InProgress')");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeExits_Tenant",
                table: "EmployeeExits",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_ExitInterviews_ExitId",
                table: "ExitInterviews",
                column: "ExitId");

            migrationBuilder.CreateIndex(
                name: "IX_ExitInterviews_Tenant",
                table: "ExitInterviews",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_ExitInterviews_Tenant_ExitId",
                table: "ExitInterviews",
                columns: new[] { "Tenant", "ExitId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClearanceItems");

            migrationBuilder.DropTable(
                name: "ExitInterviews");

            migrationBuilder.DropTable(
                name: "EmployeeExits");
        }
    }
}
