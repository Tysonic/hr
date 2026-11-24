using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyHr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Retention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AnonymisedAt",
                table: "Employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetentionYears",
                table: "CompanyProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 7); // existing companies start on the default policy
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnonymisedAt",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "RetentionYears",
                table: "CompanyProfiles");
        }
    }
}
