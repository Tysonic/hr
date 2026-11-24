using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyHr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeTaxProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Resident = table.Column<bool>(type: "boolean", nullable: false),
                    SecondaryEmployment = table.Column<bool>(type: "boolean", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeTaxProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeTaxProfiles_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Taxable = table.Column<bool>(type: "boolean", nullable: false),
                    Pensionable = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayComponents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    PayDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReversesRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CalculatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReturnNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EmployeeCount = table.Column<int>(type: "integer", nullable: false),
                    Gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Deductions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EmployerCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Warnings = table.Column<List<string>>(type: "text[]", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_PayrollRuns_ReversesRunId",
                        column: x => x.ReversesRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeePayItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeePayItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeePayItems_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeePayItems_PayComponents_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "PayComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollInputs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollInputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollInputs_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollInputs_PayComponents_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "PayComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollInputs_PayrollRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payslips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EmployeeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DepartmentName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PositionTitle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TaxNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SocialSecurityNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PaymentMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BankBranch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BankAccountNumber = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BankAccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MobileMoneyProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    MobileMoneyNumber = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Lines = table.Column<string>(type: "jsonb", nullable: false),
                    Gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxableGross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PensionableGross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EmployerContributions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Warnings = table.Column<List<string>>(type: "text[]", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payslips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payslips_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payslips_PayrollRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayItems_ComponentId",
                table: "EmployeePayItems",
                column: "ComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayItems_EmployeeId",
                table: "EmployeePayItems",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayItems_Tenant",
                table: "EmployeePayItems",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayItems_Tenant_EmployeeId",
                table: "EmployeePayItems",
                columns: new[] { "Tenant", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTaxProfiles_EmployeeId",
                table: "EmployeeTaxProfiles",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTaxProfiles_Tenant",
                table: "EmployeeTaxProfiles",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTaxProfiles_Tenant_EmployeeId",
                table: "EmployeeTaxProfiles",
                columns: new[] { "Tenant", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayComponents_Tenant",
                table: "PayComponents",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_PayComponents_Tenant_Code",
                table: "PayComponents",
                columns: new[] { "Tenant", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputs_ComponentId",
                table: "PayrollInputs",
                column: "ComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputs_EmployeeId",
                table: "PayrollInputs",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputs_RunId",
                table: "PayrollInputs",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputs_Tenant",
                table: "PayrollInputs",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollInputs_Tenant_RunId",
                table: "PayrollInputs",
                columns: new[] { "Tenant", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_ReversesRunId",
                table: "PayrollRuns",
                column: "ReversesRunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_Tenant",
                table: "PayrollRuns",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_Tenant_Year_Month",
                table: "PayrollRuns",
                columns: new[] { "Tenant", "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_EmployeeId",
                table: "Payslips",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_RunId",
                table: "Payslips",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_Tenant",
                table: "Payslips",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_Tenant_EmployeeId",
                table: "Payslips",
                columns: new[] { "Tenant", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_Tenant_RunId_EmployeeId",
                table: "Payslips",
                columns: new[] { "Tenant", "RunId", "EmployeeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeePayItems");

            migrationBuilder.DropTable(
                name: "EmployeeTaxProfiles");

            migrationBuilder.DropTable(
                name: "PayrollInputs");

            migrationBuilder.DropTable(
                name: "Payslips");

            migrationBuilder.DropTable(
                name: "PayComponents");

            migrationBuilder.DropTable(
                name: "PayrollRuns");
        }
    }
}
