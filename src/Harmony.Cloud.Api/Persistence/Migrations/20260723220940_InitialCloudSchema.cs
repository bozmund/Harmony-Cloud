using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Harmony.Cloud.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCloudSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cloud_devices",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    platform = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    app_version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    push_token_ciphertext = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    push_registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_realtime_connected = table.Column<bool>(type: "boolean", nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false),
                    last_checkpoint = table.Column<long>(type: "bigint", nullable: false),
                    sync_paused = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_devices", x => new { x.account_id, x.device_id });
                });

            migrationBuilder.CreateTable(
                name: "cloud_playback_commands",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    applied = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_playback_commands", x => new { x.account_id, x.command_id });
                });

            migrationBuilder.CreateTable(
                name: "cloud_playback_sessions",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_playback_sessions", x => new { x.account_id, x.session_id });
                });

            migrationBuilder.CreateTable(
                name: "cloud_snapshots",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    hlc_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_snapshots", x => new { x.account_id, x.entity_type, x.entity_id });
                });

            migrationBuilder.CreateTable(
                name: "cloud_sync_events",
                columns: table => new
                {
                    revision = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_sequence = table.Column<long>(type: "bigint", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    operation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_sync_events", x => x.revision);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_playback_commands_account_id_target_device_id_expires~",
                table: "cloud_playback_commands",
                columns: new[] { "account_id", "target_device_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_playback_sessions_account_id_ended_at",
                table: "cloud_playback_sessions",
                columns: new[] { "account_id", "ended_at" });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_sync_events_account_id_device_id_device_sequence",
                table: "cloud_sync_events",
                columns: new[] { "account_id", "device_id", "device_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_sync_events_account_id_event_id",
                table: "cloud_sync_events",
                columns: new[] { "account_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_sync_events_account_id_revision",
                table: "cloud_sync_events",
                columns: new[] { "account_id", "revision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cloud_devices");

            migrationBuilder.DropTable(
                name: "cloud_playback_commands");

            migrationBuilder.DropTable(
                name: "cloud_playback_sessions");

            migrationBuilder.DropTable(
                name: "cloud_snapshots");

            migrationBuilder.DropTable(
                name: "cloud_sync_events");
        }
    }
}
