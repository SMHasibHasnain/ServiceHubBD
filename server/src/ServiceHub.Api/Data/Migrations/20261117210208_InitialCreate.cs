using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ServiceHub.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:ltree", ",,");

            migrationBuilder.CreateSequence(
                name: "booking_number_seq",
                startValue: 1000L);

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role_id = table.Column<short>(type: "smallint", nullable: true),
                    scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    business_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zone_id = table.Column<int>(type: "integer", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    parent_id = table.Column<int>(type: "integer", nullable: true),
                    name_bn = table.Column<string>(type: "text", nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    icon = table.Column<string>(type: "text", nullable: true),
                    banner_url = table.Column<string>(type: "text", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_categories_categories_parent_id",
                        column: x => x.parent_id,
                        principalTable: "categories",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "geo_zones",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    parent_id = table.Column<int>(type: "integer", nullable: true),
                    level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    depth = table.Column<short>(type: "smallint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: true),
                    name_bn = table.Column<string>(type: "text", nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    path = table.Column<string>(type: "ltree", nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geo_zones", x => x.id);
                    table.ForeignKey(
                        name: "fk_geo_zones_geo_zones_parent_id",
                        column: x => x.parent_id,
                        principalTable: "geo_zones",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    module = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "booking_field_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_id = table.Column<Guid>(type: "uuid", nullable: true),
                    label = table.Column<string>(type: "text", nullable: false),
                    value_text = table.Column<string>(type: "text", nullable: true),
                    file_url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_field_responses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "booking_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    to_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    actor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_status_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_request_id = table.Column<string>(type: "text", nullable: true),
                    start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    service_name = table.Column<string>(type: "text", nullable: false),
                    duration_minutes = table.Column<short>(type: "smallint", nullable: false),
                    price_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    quoted_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    discount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    final_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    customer_name = table.Column<string>(type: "text", nullable: false),
                    customer_phone = table.Column<string>(type: "text", nullable: false),
                    service_address = table.Column<string>(type: "text", nullable: true),
                    customer_note = table.Column<string>(type: "text", nullable: true),
                    business_note = table.Column<string>(type: "text", nullable: true),
                    payment_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_time", "end_at > start_at");
                });

            migrationBuilder.CreateTable(
                name: "business_booking_settings",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    auto_confirm = table.Column<bool>(type: "boolean", nullable: false),
                    slot_interval_minutes = table.Column<short>(type: "smallint", nullable: false),
                    min_notice_minutes = table.Column<int>(type: "integer", nullable: false),
                    max_advance_days = table.Column<short>(type: "smallint", nullable: false),
                    buffer_minutes = table.Column<short>(type: "smallint", nullable: false),
                    max_concurrent_bookings = table.Column<short>(type: "smallint", nullable: false),
                    cancellation_policy = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_booking_settings", x => x.business_id);
                });

            migrationBuilder.CreateTable(
                name: "business_closures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_closures", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "business_customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    is_vip = table.Column<bool>(type: "boolean", nullable: false),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    first_booking_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_visit_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    next_booking_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    total_bookings = table.Column<int>(type: "integer", nullable: false),
                    completed_bookings = table.Column<int>(type: "integer", nullable: false),
                    lifetime_value = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    favourite_service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "business_hours",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<short>(type: "smallint", nullable: false),
                    opens_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    closes_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_hours", x => x.id);
                    table.CheckConstraint("ck_business_hours_range", "day_of_week BETWEEN 0 AND 6 AND closes_at > opens_at");
                });

            migrationBuilder.CreateTable(
                name: "business_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<short>(type: "smallint", nullable: false),
                    invitee_phone = table.Column<string>(type: "text", nullable: true),
                    invitee_email = table.Column<string>(type: "text", nullable: true),
                    invitee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_invitations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "business_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<short>(type: "smallint", nullable: false),
                    title = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: true),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    removed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_members", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "businesses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    slug = table.Column<string>(type: "character varying(170)", maxLength: 170, nullable: false),
                    tagline = table.Column<string>(type: "text", nullable: true),
                    short_description = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    established_year = table.Column<short>(type: "smallint", nullable: true),
                    logo_url = table.Column<string>(type: "text", nullable: true),
                    cover_url = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    website_url = table.Column<string>(type: "text", nullable: true),
                    facebook_url = table.Column<string>(type: "text", nullable: true),
                    address_line = table.Column<string>(type: "text", nullable: true),
                    zone_id = table.Column<int>(type: "integer", nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    coverage_note = table.Column<string>(type: "text", nullable: true),
                    languages = table.Column<List<string>>(type: "text[]", nullable: false),
                    offers_home_service = table.Column<bool>(type: "boolean", nullable: false),
                    offers_emergency_service = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    verification_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    onboarding_step = table.Column<short>(type: "smallint", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    avg_rating = table.Column<decimal>(type: "numeric(2,1)", precision: 2, scale: 1, nullable: false),
                    review_count = table.Column<int>(type: "integer", nullable: false),
                    completed_booking_count = table.Column<int>(type: "integer", nullable: false),
                    avg_response_minutes = table.Column<int>(type: "integer", nullable: true),
                    health_score = table.Column<short>(type: "smallint", nullable: true),
                    health_score_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    onboarded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_businesses", x => x.id);
                    table.ForeignKey(
                        name: "fk_businesses_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_businesses_geo_zones_zone_id",
                        column: x => x.zone_id,
                        principalTable: "geo_zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name_bn = table.Column<string>(type: "text", nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    rank = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_roles_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    short_description = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    price_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    price_max = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    duration_minutes = table.Column<short>(type: "smallint", nullable: false),
                    availability = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_featured = table.Column<bool>(type: "boolean", nullable: false),
                    featured_order = table.Column<short>(type: "smallint", nullable: true),
                    preparation_notes = table.Column<string>(type: "text", nullable: true),
                    policies = table.Column<string>(type: "text", nullable: true),
                    internal_notes = table.Column<string>(type: "text", nullable: true),
                    booking_count = table.Column<int>(type: "integer", nullable: false),
                    avg_rating = table.Column<decimal>(type: "numeric(2,1)", precision: 2, scale: 1, nullable: false),
                    view_count = table.Column<int>(type: "integer", nullable: false),
                    last_booked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                    table.CheckConstraint("ck_services_price", "price_type = 'contact_for_price' OR price IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_services_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_services_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    avatar_url = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    address_line = table.Column<string>(type: "text", nullable: true),
                    zone_id = table.Column<int>(type: "integer", nullable: true),
                    last_workspace_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_active_business_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mfa_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    phone_verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    email_verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_businesses_last_active_business_id",
                        column: x => x.last_active_business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_users_geo_zones_zone_id",
                        column: x => x.zone_id,
                        principalTable: "geo_zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_id = table.Column<short>(type: "smallint", nullable: false),
                    permission_id = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_booking_fields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    field_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    options = table.Column<List<string>>(type: "text[]", nullable: true),
                    placeholder = table.Column<string>(type: "text", nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_booking_fields", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_booking_fields_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_notes_business_customers_business_customer_id",
                        column: x => x.business_customer_id,
                        principalTable: "business_customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_customer_notes_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "favourites",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favourites", x => new { x.user_id, x.business_id });
                    table.ForeignKey(
                        name: "fk_favourites_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_favourites_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: true),
                    action_url = table.Column<string>(type: "text", nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notifications_users_recipient_id",
                        column: x => x.recipient_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_role_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<short>(type: "smallint", nullable: false),
                    zone_id = table.Column<int>(type: "integer", nullable: true),
                    category_id = table.Column<int>(type: "integer", nullable: true),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_role_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_role_assignments_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_platform_role_assignments_geo_zones_zone_id",
                        column: x => x.zone_id,
                        principalTable: "geo_zones",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_platform_role_assignments_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_platform_role_assignments_users_granted_by",
                        column: x => x.granted_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_role_assignments_users_revoked_by",
                        column: x => x.revoked_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_role_assignments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<short>(type: "smallint", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.id);
                    table.CheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_reviews_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_users_customer_id",
                        column: x => x.customer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refresh_token_hash = table.Column<string>(type: "text", nullable: false),
                    device_name = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<string>(type: "text", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    remember_me = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "verification_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target = table.Column<string>(type: "text", nullable: false),
                    purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<short>(type: "smallint", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verification_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_verification_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "verification_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_no = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    priority = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    assigned_moderator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requires_inspection = table.Column<bool>(type: "boolean", nullable: false),
                    business_snapshot = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    decision_reason = table.Column<string>(type: "text", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    review_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verification_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_verification_requests_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_verification_requests_users_assigned_moderator_id",
                        column: x => x.assigned_moderator_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_verification_requests_users_submitted_by",
                        column: x => x.submitted_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_business_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_business_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_review_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    severity = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    assigned_moderator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_note = table.Column<string>(type: "text", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reports", x => x.id);
                    table.CheckConstraint("ck_reports_target", "(target_type = 'business' AND target_business_id IS NOT NULL AND target_review_id IS NULL AND target_user_id IS NULL) OR (target_type = 'review' AND target_review_id IS NOT NULL AND target_business_id IS NULL AND target_user_id IS NULL) OR (target_type = 'user' AND target_user_id IS NOT NULL AND target_business_id IS NULL AND target_review_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_reports_businesses_reporter_business_id",
                        column: x => x.reporter_business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_reports_businesses_target_business_id",
                        column: x => x.target_business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_reports_reviews_target_review_id",
                        column: x => x.target_review_id,
                        principalTable: "reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_reports_users_assigned_moderator_id",
                        column: x => x.assigned_moderator_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_reports_users_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reports_users_target_user_id",
                        column: x => x.target_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "review_replies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_replies", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_replies_reviews_review_id",
                        column: x => x.review_id,
                        principalTable: "reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_review_replies_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "verification_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    verification_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    file_url = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: true),
                    mime_type = table.Column<string>(type: "text", nullable: true),
                    file_size_bytes = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reviewer_note = table.Column<string>(type: "text", nullable: true),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verification_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_verification_documents_verification_requests_verification_r",
                        column: x => x.verification_request_id,
                        principalTable: "verification_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inspections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    verification_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    report_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inspector_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    scheduled_for = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    visited_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    visit_latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    visit_longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    findings = table.Column<string>(type: "text", nullable: true),
                    checklist = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspections", x => x.id);
                    table.ForeignKey(
                        name: "fk_inspections_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inspections_geo_zones_zone_id",
                        column: x => x.zone_id,
                        principalTable: "geo_zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inspections_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_inspections_users_inspector_id",
                        column: x => x.inspector_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_inspections_users_requested_by",
                        column: x => x.requested_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inspections_verification_requests_verification_request_id",
                        column: x => x.verification_request_id,
                        principalTable: "verification_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "moderation_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    moderator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    verification_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    business_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_id = table.Column<Guid>(type: "uuid", nullable: true),
                    report_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inspection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_moderation_actions_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_moderation_actions_inspections_inspection_id",
                        column: x => x.inspection_id,
                        principalTable: "inspections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_moderation_actions_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_moderation_actions_reviews_review_id",
                        column: x => x.review_id,
                        principalTable: "reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_moderation_actions_users_moderator_id",
                        column: x => x.moderator_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_actions_verification_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "verification_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_moderation_actions_verification_requests_verification_reque",
                        column: x => x.verification_request_id,
                        principalTable: "verification_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_actor_id_created_at",
                table: "audit_logs",
                columns: new[] { "actor_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_business_id_created_at",
                table: "audit_logs",
                columns: new[] { "business_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_created_at",
                table: "audit_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_booking_field_responses_booking_id_field_id",
                table: "booking_field_responses",
                columns: new[] { "booking_id", "field_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_booking_field_responses_field_id",
                table: "booking_field_responses",
                column: "field_id");

            migrationBuilder.CreateIndex(
                name: "ix_booking_status_history_booking_id_created_at",
                table: "booking_status_history",
                columns: new[] { "booking_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_booking_status_history_changed_by",
                table: "booking_status_history",
                column: "changed_by");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_assigned_member_id_start_at",
                table: "bookings",
                columns: new[] { "assigned_member_id", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_booking_number",
                table: "bookings",
                column: "booking_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_business_customer_id",
                table: "bookings",
                column: "business_customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_business_id_start_at",
                table: "bookings",
                columns: new[] { "business_id", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_business_id_status_start_at",
                table: "bookings",
                columns: new[] { "business_id", "status", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_customer_user_id_client_request_id",
                table: "bookings",
                columns: new[] { "customer_user_id", "client_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_customer_user_id_start_at",
                table: "bookings",
                columns: new[] { "customer_user_id", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_service_id",
                table: "bookings",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_business_closures_business_id_closed_on",
                table: "business_closures",
                columns: new[] { "business_id", "closed_on" });

            migrationBuilder.CreateIndex(
                name: "ix_business_customers_business_id_is_vip_last_visit_at",
                table: "business_customers",
                columns: new[] { "business_id", "is_vip", "last_visit_at" });

            migrationBuilder.CreateIndex(
                name: "ix_business_customers_business_id_phone",
                table: "business_customers",
                columns: new[] { "business_id", "phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_customers_business_id_user_id",
                table: "business_customers",
                columns: new[] { "business_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_customers_favourite_service_id",
                table: "business_customers",
                column: "favourite_service_id");

            migrationBuilder.CreateIndex(
                name: "ix_business_customers_user_id",
                table: "business_customers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_business_hours_business_id_day_of_week_opens_at",
                table: "business_hours",
                columns: new[] { "business_id", "day_of_week", "opens_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_invitations_business_id_status",
                table: "business_invitations",
                columns: new[] { "business_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_business_invitations_invitee_phone",
                table: "business_invitations",
                column: "invitee_phone");

            migrationBuilder.CreateIndex(
                name: "ix_business_invitations_role_id",
                table: "business_invitations",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_business_invitations_token_hash",
                table: "business_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_members_business_id_role_id",
                table: "business_members",
                columns: new[] { "business_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_business_members_business_id_user_id",
                table: "business_members",
                columns: new[] { "business_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_members_role_id",
                table: "business_members",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_business_members_user_id",
                table: "business_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_businesses_category_id_status",
                table: "businesses",
                columns: new[] { "category_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_businesses_onboarded_by",
                table: "businesses",
                column: "onboarded_by");

            migrationBuilder.CreateIndex(
                name: "ix_businesses_owner_id",
                table: "businesses",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_businesses_slug",
                table: "businesses",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_businesses_verification_status",
                table: "businesses",
                column: "verification_status");

            migrationBuilder.CreateIndex(
                name: "ix_businesses_zone_id_status",
                table: "businesses",
                columns: new[] { "zone_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_id_sort_order",
                table: "categories",
                columns: new[] { "parent_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_slug",
                table: "categories",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_notes_author_id",
                table: "customer_notes",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_notes_business_customer_id",
                table: "customer_notes",
                column: "business_customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_favourites_business_id",
                table: "favourites",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "ix_geo_zones_code",
                table: "geo_zones",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_geo_zones_parent_id",
                table: "geo_zones",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_geo_zones_path",
                table: "geo_zones",
                column: "path")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_geo_zones_slug",
                table: "geo_zones",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inspections_business_id",
                table: "inspections",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "ix_inspections_inspector_id_status_scheduled_for",
                table: "inspections",
                columns: new[] { "inspector_id", "status", "scheduled_for" });

            migrationBuilder.CreateIndex(
                name: "ix_inspections_report_id",
                table: "inspections",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "ix_inspections_requested_by",
                table: "inspections",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ix_inspections_verification_request_id",
                table: "inspections",
                column: "verification_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_inspections_zone_id_status",
                table: "inspections",
                columns: new[] { "zone_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_actions_business_id",
                table: "moderation_actions",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_actions_document_id",
                table: "moderation_actions",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_actions_inspection_id",
                table: "moderation_actions",
                column: "inspection_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_actions_moderator_id_created_at",
                table: "moderation_actions",
                columns: new[] { "moderator_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_actions_report_id",
                table: "moderation_actions",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_actions_review_id",
                table: "moderation_actions",
                column: "review_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_actions_verification_request_id",
                table: "moderation_actions",
                column: "verification_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_business_id",
                table: "notifications",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_id_created_at",
                table: "notifications",
                columns: new[] { "recipient_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_id_workspace_business_id_read_at",
                table: "notifications",
                columns: new[] { "recipient_id", "workspace", "business_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ix_permissions_key",
                table: "permissions",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_role_assignments_category_id",
                table: "platform_role_assignments",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_role_assignments_granted_by",
                table: "platform_role_assignments",
                column: "granted_by");

            migrationBuilder.CreateIndex(
                name: "ix_platform_role_assignments_revoked_by",
                table: "platform_role_assignments",
                column: "revoked_by");

            migrationBuilder.CreateIndex(
                name: "ix_platform_role_assignments_role_id_zone_id",
                table: "platform_role_assignments",
                columns: new[] { "role_id", "zone_id" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_role_assignments_user_id_role_id_zone_id_category_",
                table: "platform_role_assignments",
                columns: new[] { "user_id", "role_id", "zone_id", "category_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_platform_role_assignments_zone_id",
                table: "platform_role_assignments",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_assigned_moderator_id",
                table: "reports",
                column: "assigned_moderator_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_reporter_business_id",
                table: "reports",
                column: "reporter_business_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_reporter_id",
                table: "reports",
                column: "reporter_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_status_severity_created_at",
                table: "reports",
                columns: new[] { "status", "severity", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reports_target_business_id",
                table: "reports",
                column: "target_business_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_target_review_id",
                table: "reports",
                column: "target_review_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_target_user_id",
                table: "reports",
                column: "target_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_replies_author_id",
                table: "review_replies",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_replies_review_id",
                table: "review_replies",
                column: "review_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reviews_booking_id",
                table: "reviews",
                column: "booking_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reviews_business_id_status_created_at",
                table: "reviews",
                columns: new[] { "business_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_customer_id",
                table: "reviews",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_service_id",
                table: "reviews",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_id",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_business_id_key",
                table: "roles",
                columns: new[] { "business_id", "key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_roles_scope",
                table: "roles",
                column: "scope");

            migrationBuilder.CreateIndex(
                name: "ix_service_booking_fields_service_id",
                table: "service_booking_fields",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_services_business_id_slug",
                table: "services",
                columns: new[] { "business_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_services_business_id_status",
                table: "services",
                columns: new[] { "business_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_services_category_id",
                table: "services",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_refresh_token_hash",
                table: "user_sessions",
                column: "refresh_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_id",
                table: "user_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_last_active_business_id",
                table: "users",
                column: "last_active_business_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_phone",
                table: "users",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_status",
                table: "users",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_users_zone_id",
                table: "users",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_codes_target_purpose",
                table: "verification_codes",
                columns: new[] { "target", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ix_verification_codes_user_id",
                table: "verification_codes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_documents_verification_request_id",
                table: "verification_documents",
                column: "verification_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_requests_assigned_moderator_id",
                table: "verification_requests",
                column: "assigned_moderator_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_requests_business_id",
                table: "verification_requests",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_requests_status_priority_submitted_at",
                table: "verification_requests",
                columns: new[] { "status", "priority", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_verification_requests_submitted_by",
                table: "verification_requests",
                column: "submitted_by");

            migrationBuilder.AddForeignKey(
                name: "fk_booking_field_responses_bookings_booking_id",
                table: "booking_field_responses",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_booking_field_responses_service_booking_fields_field_id",
                table: "booking_field_responses",
                column: "field_id",
                principalTable: "service_booking_fields",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_booking_status_history_bookings_booking_id",
                table: "booking_status_history",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_booking_status_history_users_changed_by",
                table: "booking_status_history",
                column: "changed_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_bookings_business_customers_business_customer_id",
                table: "bookings",
                column: "business_customer_id",
                principalTable: "business_customers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_bookings_business_members_assigned_member_id",
                table: "bookings",
                column: "assigned_member_id",
                principalTable: "business_members",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_bookings_businesses_business_id",
                table: "bookings",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_bookings_services_service_id",
                table: "bookings",
                column: "service_id",
                principalTable: "services",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_bookings_users_customer_user_id",
                table: "bookings",
                column: "customer_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_business_booking_settings_businesses_business_id",
                table: "business_booking_settings",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_business_closures_businesses_business_id",
                table: "business_closures",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_business_customers_businesses_business_id",
                table: "business_customers",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_business_customers_services_favourite_service_id",
                table: "business_customers",
                column: "favourite_service_id",
                principalTable: "services",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_business_customers_users_user_id",
                table: "business_customers",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_business_hours_businesses_business_id",
                table: "business_hours",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_business_invitations_businesses_business_id",
                table: "business_invitations",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_business_invitations_roles_role_id",
                table: "business_invitations",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_business_members_businesses_business_id",
                table: "business_members",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_business_members_roles_role_id",
                table: "business_members",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_business_members_users_user_id",
                table: "business_members",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_businesses_users_onboarded_by",
                table: "businesses",
                column: "onboarded_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_businesses_users_owner_id",
                table: "businesses",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_businesses_users_onboarded_by",
                table: "businesses");

            migrationBuilder.DropForeignKey(
                name: "fk_businesses_users_owner_id",
                table: "businesses");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "booking_field_responses");

            migrationBuilder.DropTable(
                name: "booking_status_history");

            migrationBuilder.DropTable(
                name: "business_booking_settings");

            migrationBuilder.DropTable(
                name: "business_closures");

            migrationBuilder.DropTable(
                name: "business_hours");

            migrationBuilder.DropTable(
                name: "business_invitations");

            migrationBuilder.DropTable(
                name: "customer_notes");

            migrationBuilder.DropTable(
                name: "favourites");

            migrationBuilder.DropTable(
                name: "moderation_actions");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "platform_role_assignments");

            migrationBuilder.DropTable(
                name: "review_replies");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "user_sessions");

            migrationBuilder.DropTable(
                name: "verification_codes");

            migrationBuilder.DropTable(
                name: "service_booking_fields");

            migrationBuilder.DropTable(
                name: "inspections");

            migrationBuilder.DropTable(
                name: "verification_documents");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropTable(
                name: "verification_requests");

            migrationBuilder.DropTable(
                name: "reviews");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "business_customers");

            migrationBuilder.DropTable(
                name: "business_members");

            migrationBuilder.DropTable(
                name: "services");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "businesses");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "geo_zones");

            migrationBuilder.DropSequence(
                name: "booking_number_seq");
        }
    }
}
