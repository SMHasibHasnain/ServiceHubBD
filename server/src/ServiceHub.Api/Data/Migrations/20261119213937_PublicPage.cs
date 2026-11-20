using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PublicPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "image_url",
                table: "services",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "header_style",
                table: "businesses",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "page_layout",
                table: "businesses",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "theme_key",
                table: "businesses",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "show_on_profile",
                table: "business_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "business_faqs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    answer = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    sort_order = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_faqs", x => x.id);
                    table.ForeignKey(
                        name: "fk_business_faqs_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_highlights",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    icon_key = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    sort_order = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_highlights", x => x.id);
                    table.ForeignKey(
                        name: "fk_business_highlights_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_notices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_notices", x => x.id);
                    table.ForeignKey(
                        name: "fk_business_notices_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_business_notices_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "gallery_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    caption = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    sort_order = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gallery_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_gallery_items_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_business_faqs_business_id_sort_order",
                table: "business_faqs",
                columns: new[] { "business_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_business_highlights_business_id_sort_order",
                table: "business_highlights",
                columns: new[] { "business_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_business_notices_business_id_is_published_created_at",
                table: "business_notices",
                columns: new[] { "business_id", "is_published", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_business_notices_created_by",
                table: "business_notices",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_gallery_items_business_id_sort_order",
                table: "gallery_items",
                columns: new[] { "business_id", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_faqs");

            migrationBuilder.DropTable(
                name: "business_highlights");

            migrationBuilder.DropTable(
                name: "business_notices");

            migrationBuilder.DropTable(
                name: "gallery_items");

            migrationBuilder.DropColumn(
                name: "image_url",
                table: "services");

            migrationBuilder.DropColumn(
                name: "header_style",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "page_layout",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "theme_key",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "show_on_profile",
                table: "business_members");
        }
    }
}
