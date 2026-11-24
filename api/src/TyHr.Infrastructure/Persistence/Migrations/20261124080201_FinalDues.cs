using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyHr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinalDues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinalSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExitId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Lines = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreparedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tenant = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinalSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinalSettlements_EmployeeExits_ExitId",
                        column: x => x.ExitId,
                        principalTable: "EmployeeExits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinalSettlements_ExitId",
                table: "FinalSettlements",
                column: "ExitId");

            migrationBuilder.CreateIndex(
                name: "IX_FinalSettlements_Tenant",
                table: "FinalSettlements",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_FinalSettlements_Tenant_ExitId",
                table: "FinalSettlements",
                columns: new[] { "Tenant", "ExitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinalSettlements_Tenant_RunId",
                table: "FinalSettlements",
                columns: new[] { "Tenant", "RunId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinalSettlements");
        }
    }
}
