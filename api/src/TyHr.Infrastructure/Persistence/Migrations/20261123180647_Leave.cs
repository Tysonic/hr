using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyHr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Leave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkingDays",
                table: "CompanyProfiles",
                type: "integer",
                nullable: false,
                // Existing companies work Monday to Friday (WorkWeek mask: Mon 2 + Tue 4 + Wed 8 + Thu 16 + Fri 32).
                defaultValue: 62);

            migrationBuilder.CreateTable(
                name: "LeaveTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Accrual = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DaysPerYear = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    MaxDaysPerRequest = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    CarryForwardMax = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    CountsCalendarDays = table.Column<bool>(type: "boolean", nullable: false),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false),
                    EligibleGender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    MinServiceMonths = table.Column<int>(type: "integer", nullable: false),
                    DocumentRequiredAfterDays = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublicHolidays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicHolidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaveAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaveYear = table.Column<int>(type: "integer", nullable: false),
                    Days = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveAdjustments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveAdjustments_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "LeaveTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    HalfDay = table.Column<bool>(type: "boolean", nullable: false),
                    Days = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_LeaveRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_EmployeeDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "EmployeeDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "LeaveTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAdjustments_EmployeeId",
                table: "LeaveAdjustments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAdjustments_LeaveTypeId",
                table: "LeaveAdjustments",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAdjustments_Tenant",
                table: "LeaveAdjustments",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAdjustments_Tenant_EmployeeId_LeaveTypeId_LeaveYear",
                table: "LeaveAdjustments",
                columns: new[] { "Tenant", "EmployeeId", "LeaveTypeId", "LeaveYear" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_DocumentId",
                table: "LeaveRequests",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_EmployeeId",
                table: "LeaveRequests",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_LeaveTypeId",
                table: "LeaveRequests",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_Tenant",
                table: "LeaveRequests",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_Tenant_EmployeeId_StartDate",
                table: "LeaveRequests",
                columns: new[] { "Tenant", "EmployeeId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_Tenant_Status_StartDate",
                table: "LeaveRequests",
                columns: new[] { "Tenant", "Status", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_Tenant",
                table: "LeaveTypes",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_Tenant_Code",
                table: "LeaveTypes",
                columns: new[] { "Tenant", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicHolidays_Tenant",
                table: "PublicHolidays",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_PublicHolidays_Tenant_Date",
                table: "PublicHolidays",
                columns: new[] { "Tenant", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaveAdjustments");

            migrationBuilder.DropTable(
                name: "LeaveRequests");

            migrationBuilder.DropTable(
                name: "PublicHolidays");

            migrationBuilder.DropTable(
                name: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "WorkingDays",
                table: "CompanyProfiles");
        }
    }
}
