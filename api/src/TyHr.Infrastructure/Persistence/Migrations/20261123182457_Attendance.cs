using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyHr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceUserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceEnrollments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClockEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    AccuracyMeters = table.Column<double>(type: "double precision", nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClockEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClockEvents_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    BreakMinutes = table.Column<int>(type: "integer", nullable: false),
                    WorkingDays = table.Column<int>(type: "integer", nullable: false),
                    GraceMinutes = table.Column<int>(type: "integer", nullable: false),
                    OvertimeMinimumMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleAssignments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleAssignments_WorkSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "WorkSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceEnrollments_EmployeeId",
                table: "AttendanceEnrollments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceEnrollments_Tenant",
                table: "AttendanceEnrollments",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceEnrollments_Tenant_DeviceUserId",
                table: "AttendanceEnrollments",
                columns: new[] { "Tenant", "DeviceUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceEnrollments_Tenant_EmployeeId",
                table: "AttendanceEnrollments",
                columns: new[] { "Tenant", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClockEvents_EmployeeId",
                table: "ClockEvents",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ClockEvents_Tenant",
                table: "ClockEvents",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_ClockEvents_Tenant_EmployeeId_Time",
                table: "ClockEvents",
                columns: new[] { "Tenant", "EmployeeId", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleAssignments_EmployeeId",
                table: "ScheduleAssignments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleAssignments_ScheduleId",
                table: "ScheduleAssignments",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleAssignments_Tenant",
                table: "ScheduleAssignments",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleAssignments_Tenant_EmployeeId_EffectiveFrom",
                table: "ScheduleAssignments",
                columns: new[] { "Tenant", "EmployeeId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkSchedules_Tenant",
                table: "WorkSchedules",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSchedules_Tenant_Name",
                table: "WorkSchedules",
                columns: new[] { "Tenant", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceEnrollments");

            migrationBuilder.DropTable(
                name: "ClockEvents");

            migrationBuilder.DropTable(
                name: "ScheduleAssignments");

            migrationBuilder.DropTable(
                name: "WorkSchedules");
        }
    }
}
