using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyHr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SelfService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Announcements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Pinned = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    PublishedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Announcements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProfileChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NextOfKinId = table.Column<Guid>(type: "uuid", nullable: true),
                    Before = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Proposed = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_ProfileChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileChangeRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_Tenant",
                table: "Announcements",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_Tenant_ExpiresOn",
                table: "Announcements",
                columns: new[] { "Tenant", "ExpiresOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileChangeRequests_EmployeeId",
                table: "ProfileChangeRequests",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileChangeRequests_Tenant",
                table: "ProfileChangeRequests",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileChangeRequests_Tenant_EmployeeId",
                table: "ProfileChangeRequests",
                columns: new[] { "Tenant", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileChangeRequests_Tenant_Status_Kind",
                table: "ProfileChangeRequests",
                columns: new[] { "Tenant", "Status", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Announcements");

            migrationBuilder.DropTable(
                name: "ProfileChangeRequests");
        }
    }
}
