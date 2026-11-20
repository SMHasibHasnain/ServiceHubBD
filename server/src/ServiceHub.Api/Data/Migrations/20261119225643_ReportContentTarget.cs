using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportContentTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "target_content_id",
                table: "reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_content_type",
                table: "reports",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "target_content_id",
                table: "reports");

            migrationBuilder.DropColumn(
                name: "target_content_type",
                table: "reports");
        }
    }
}
