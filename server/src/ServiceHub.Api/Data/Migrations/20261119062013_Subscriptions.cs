using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ServiceHub.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Subscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "invoice_number_seq");

            migrationBuilder.CreateTable(
                name: "subscription_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    from_plan = table.Column<string>(type: "text", nullable: true),
                    to_plan = table.Column<string>(type: "text", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_events_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscription_plans",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name_bn = table.Column<string>(type: "text", nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: false),
                    tagline_bn = table.Column<string>(type: "text", nullable: true),
                    price_monthly = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    price_yearly = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    sort_order = table.Column<short>(type: "smallint", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "business_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cycle = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    trial_ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    current_period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    current_period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancel_at_period_end = table.Column<bool>(type: "boolean", nullable: false),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_business_subscriptions_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_business_subscriptions_subscription_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_entitlements",
                columns: table => new
                {
                    plan_id = table.Column<short>(type: "smallint", nullable: false),
                    key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_entitlements", x => new { x.plan_id, x.key });
                    table.ForeignKey(
                        name: "fk_plan_entitlements_subscription_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscription_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<short>(type: "smallint", nullable: false),
                    cycle = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    credit = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_invoices_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subscription_invoices_subscription_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscription_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    transaction_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    sender_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_payments_subscription_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "subscription_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_business_subscriptions_business_id",
                table: "business_subscriptions",
                column: "business_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_subscriptions_plan_id",
                table: "business_subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_business_subscriptions_status_current_period_end",
                table: "business_subscriptions",
                columns: new[] { "status", "current_period_end" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_events_business_id_created_at",
                table: "subscription_events",
                columns: new[] { "business_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_events_business_id_type",
                table: "subscription_events",
                columns: new[] { "business_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_business_id_created_at",
                table: "subscription_invoices",
                columns: new[] { "business_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_number",
                table: "subscription_invoices",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_plan_id",
                table: "subscription_invoices",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_invoice_id",
                table: "subscription_payments",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_method_transaction_id",
                table: "subscription_payments",
                columns: new[] { "method", "transaction_id" },
                unique: true,
                filter: "transaction_id IS NOT NULL AND status <> 'rejected'");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_status_created_at",
                table: "subscription_payments",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_plans_key",
                table: "subscription_plans",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_subscriptions");

            migrationBuilder.DropTable(
                name: "plan_entitlements");

            migrationBuilder.DropTable(
                name: "subscription_events");

            migrationBuilder.DropTable(
                name: "subscription_payments");

            migrationBuilder.DropTable(
                name: "subscription_invoices");

            migrationBuilder.DropTable(
                name: "subscription_plans");

            migrationBuilder.DropSequence(
                name: "invoice_number_seq");
        }
    }
}
