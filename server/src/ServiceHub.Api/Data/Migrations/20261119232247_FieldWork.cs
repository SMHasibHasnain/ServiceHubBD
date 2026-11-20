using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FieldWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "notified_at",
                table: "business_notices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "progress",
                table: "bookings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "progress_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "booking_reschedule_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    proposed_start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    proposed_end_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    previous_start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    response_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    responded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    responded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_reschedule_requests", x => x.id);
                    table.CheckConstraint("ck_reschedule_range", "proposed_end_at > proposed_start_at");
                    table.ForeignKey(
                        name: "fk_booking_reschedule_requests_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_booking_reschedule_requests_users_requested_by",
                        column: x => x.requested_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_booking_reschedule_requests_users_responded_by",
                        column: x => x.responded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "inspection_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inspection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    caption = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspection_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_inspection_photos_inspections_inspection_id",
                        column: x => x.inspection_id,
                        principalTable: "inspections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sort_order = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_photos_reviews_review_id",
                        column: x => x.review_id,
                        principalTable: "reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff_leaves",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_leaves", x => x.id);
                    table.CheckConstraint("ck_staff_leaves_range", "end_date >= start_date");
                    table.ForeignKey(
                        name: "fk_staff_leaves_business_members_member_id",
                        column: x => x.member_id,
                        principalTable: "business_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_staff_leaves_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_staff_leaves_users_decided_by",
                        column: x => x.decided_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_booking_reschedule_requests_booking_id_status",
                table: "booking_reschedule_requests",
                columns: new[] { "booking_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_booking_reschedule_requests_requested_by",
                table: "booking_reschedule_requests",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ix_booking_reschedule_requests_responded_by",
                table: "booking_reschedule_requests",
                column: "responded_by");

            migrationBuilder.CreateIndex(
                name: "ix_inspection_photos_inspection_id",
                table: "inspection_photos",
                column: "inspection_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_photos_review_id_sort_order",
                table: "review_photos",
                columns: new[] { "review_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_leaves_business_id_status_start_date",
                table: "staff_leaves",
                columns: new[] { "business_id", "status", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_leaves_decided_by",
                table: "staff_leaves",
                column: "decided_by");

            migrationBuilder.CreateIndex(
                name: "ix_staff_leaves_member_id_status",
                table: "staff_leaves",
                columns: new[] { "member_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_reschedule_requests");

            migrationBuilder.DropTable(
                name: "inspection_photos");

            migrationBuilder.DropTable(
                name: "review_photos");

            migrationBuilder.DropTable(
                name: "staff_leaves");

            migrationBuilder.DropColumn(
                name: "notified_at",
                table: "business_notices");

            migrationBuilder.DropColumn(
                name: "progress",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "progress_at",
                table: "bookings");
        }
    }
}
