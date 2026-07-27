using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Harmony.Cloud.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerDomainSyncTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cloud_snapshots");

            migrationBuilder.DropTable(
                name: "cloud_sync_events");

            migrationBuilder.CreateSequence(
                name: "cloud_revision_seq");

            migrationBuilder.CreateTable(
                name: "cloud_albums",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    browse_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    artists = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    year = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    thumbnail_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    audio_playlist_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    hlc_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_albums", x => new { x.account_id, x.browse_id });
                });

            migrationBuilder.CreateTable(
                name: "cloud_artists",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    browse_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    radio_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    subscribers = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    thumbnail_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    hlc_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_artists", x => new { x.account_id, x.browse_id });
                });

            migrationBuilder.CreateTable(
                name: "cloud_blacklisted_playlists",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    playlist_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    details = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    hlc_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_blacklisted_playlists", x => new { x.account_id, x.playlist_id });
                });

            migrationBuilder.CreateTable(
                name: "cloud_events_albums",
                columns: table => new
                {
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('\"cloud_revision_seq\"')"),
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
                    table.PrimaryKey("PK_cloud_events_albums", x => x.revision);
                });

            migrationBuilder.CreateTable(
                name: "cloud_events_artists",
                columns: table => new
                {
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('\"cloud_revision_seq\"')"),
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
                    table.PrimaryKey("PK_cloud_events_artists", x => x.revision);
                });

            migrationBuilder.CreateTable(
                name: "cloud_events_blacklisted_playlists",
                columns: table => new
                {
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('\"cloud_revision_seq\"')"),
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
                    table.PrimaryKey("PK_cloud_events_blacklisted_playlists", x => x.revision);
                });

            migrationBuilder.CreateTable(
                name: "cloud_events_favourites",
                columns: table => new
                {
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('\"cloud_revision_seq\"')"),
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
                    table.PrimaryKey("PK_cloud_events_favourites", x => x.revision);
                });

            migrationBuilder.CreateTable(
                name: "cloud_events_playlist_songs",
                columns: table => new
                {
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('\"cloud_revision_seq\"')"),
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
                    table.PrimaryKey("PK_cloud_events_playlist_songs", x => x.revision);
                });

            migrationBuilder.CreateTable(
                name: "cloud_events_playlists",
                columns: table => new
                {
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('\"cloud_revision_seq\"')"),
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
                    table.PrimaryKey("PK_cloud_events_playlists", x => x.revision);
                });

            migrationBuilder.CreateTable(
                name: "cloud_events_recently_played",
                columns: table => new
                {
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('\"cloud_revision_seq\"')"),
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
                    table.PrimaryKey("PK_cloud_events_recently_played", x => x.revision);
                });

            migrationBuilder.CreateTable(
                name: "cloud_events_saved_searches",
                columns: table => new
                {
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('\"cloud_revision_seq\"')"),
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
                    table.PrimaryKey("PK_cloud_events_saved_searches", x => x.revision);
                });

            migrationBuilder.CreateTable(
                name: "cloud_events_search_history",
                columns: table => new
                {
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('\"cloud_revision_seq\"')"),
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
                    table.PrimaryKey("PK_cloud_events_search_history", x => x.revision);
                });

            migrationBuilder.CreateTable(
                name: "cloud_events_settings",
                columns: table => new
                {
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('\"cloud_revision_seq\"')"),
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
                    table.PrimaryKey("PK_cloud_events_settings", x => x.revision);
                });

            migrationBuilder.CreateTable(
                name: "cloud_favourites",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    video_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    hlc_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_favourites", x => new { x.account_id, x.video_id });
                });

            migrationBuilder.CreateTable(
                name: "cloud_playlist_songs",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    playlist_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entry_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    video_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    hlc_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_playlist_songs", x => new { x.account_id, x.playlist_id, x.entry_key });
                });

            migrationBuilder.CreateTable(
                name: "cloud_playlists",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    playlist_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    thumbnail_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    item_count = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    is_piped_playlist = table.Column<bool>(type: "boolean", nullable: false),
                    is_cloud_playlist = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    hlc_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_playlists", x => new { x.account_id, x.playlist_id });
                });

            migrationBuilder.CreateTable(
                name: "cloud_recently_played",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entry_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    video_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    hlc_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_recently_played", x => new { x.account_id, x.entry_key });
                });

            migrationBuilder.CreateTable(
                name: "cloud_saved_searches",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entry_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    query = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    hlc_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_saved_searches", x => new { x.account_id, x.entry_key });
                });

            migrationBuilder.CreateTable(
                name: "cloud_search_history",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entry_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    query = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    hlc_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_search_history", x => new { x.account_id, x.entry_key });
                });

            migrationBuilder.CreateTable(
                name: "cloud_settings",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    hlc_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_settings", x => new { x.account_id, x.key });
                });

            migrationBuilder.CreateTable(
                name: "cloud_songs",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    video_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    artists = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    album = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    duration_s = table.Column<int>(type: "integer", nullable: true),
                    artwork_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    year = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    date_ms = table.Column<long>(type: "bigint", nullable: true),
                    track_details = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    hlc_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_songs", x => new { x.account_id, x.video_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_albums_account_id_event_id",
                table: "cloud_events_albums",
                columns: new[] { "account_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_albums_account_id_revision",
                table: "cloud_events_albums",
                columns: new[] { "account_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_artists_account_id_event_id",
                table: "cloud_events_artists",
                columns: new[] { "account_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_artists_account_id_revision",
                table: "cloud_events_artists",
                columns: new[] { "account_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_blacklisted_playlists_account_id_event_id",
                table: "cloud_events_blacklisted_playlists",
                columns: new[] { "account_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_blacklisted_playlists_account_id_revision",
                table: "cloud_events_blacklisted_playlists",
                columns: new[] { "account_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_favourites_account_id_event_id",
                table: "cloud_events_favourites",
                columns: new[] { "account_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_favourites_account_id_revision",
                table: "cloud_events_favourites",
                columns: new[] { "account_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_playlist_songs_account_id_event_id",
                table: "cloud_events_playlist_songs",
                columns: new[] { "account_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_playlist_songs_account_id_revision",
                table: "cloud_events_playlist_songs",
                columns: new[] { "account_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_playlists_account_id_event_id",
                table: "cloud_events_playlists",
                columns: new[] { "account_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_playlists_account_id_revision",
                table: "cloud_events_playlists",
                columns: new[] { "account_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_recently_played_account_id_event_id",
                table: "cloud_events_recently_played",
                columns: new[] { "account_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_recently_played_account_id_revision",
                table: "cloud_events_recently_played",
                columns: new[] { "account_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_saved_searches_account_id_event_id",
                table: "cloud_events_saved_searches",
                columns: new[] { "account_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_saved_searches_account_id_revision",
                table: "cloud_events_saved_searches",
                columns: new[] { "account_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_search_history_account_id_event_id",
                table: "cloud_events_search_history",
                columns: new[] { "account_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_search_history_account_id_revision",
                table: "cloud_events_search_history",
                columns: new[] { "account_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_settings_account_id_event_id",
                table: "cloud_events_settings",
                columns: new[] { "account_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_events_settings_account_id_revision",
                table: "cloud_events_settings",
                columns: new[] { "account_id", "revision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cloud_albums");

            migrationBuilder.DropTable(
                name: "cloud_artists");

            migrationBuilder.DropTable(
                name: "cloud_blacklisted_playlists");

            migrationBuilder.DropTable(
                name: "cloud_events_albums");

            migrationBuilder.DropTable(
                name: "cloud_events_artists");

            migrationBuilder.DropTable(
                name: "cloud_events_blacklisted_playlists");

            migrationBuilder.DropTable(
                name: "cloud_events_favourites");

            migrationBuilder.DropTable(
                name: "cloud_events_playlist_songs");

            migrationBuilder.DropTable(
                name: "cloud_events_playlists");

            migrationBuilder.DropTable(
                name: "cloud_events_recently_played");

            migrationBuilder.DropTable(
                name: "cloud_events_saved_searches");

            migrationBuilder.DropTable(
                name: "cloud_events_search_history");

            migrationBuilder.DropTable(
                name: "cloud_events_settings");

            migrationBuilder.DropTable(
                name: "cloud_favourites");

            migrationBuilder.DropTable(
                name: "cloud_playlist_songs");

            migrationBuilder.DropTable(
                name: "cloud_playlists");

            migrationBuilder.DropTable(
                name: "cloud_recently_played");

            migrationBuilder.DropTable(
                name: "cloud_saved_searches");

            migrationBuilder.DropTable(
                name: "cloud_search_history");

            migrationBuilder.DropTable(
                name: "cloud_settings");

            migrationBuilder.DropTable(
                name: "cloud_songs");

            migrationBuilder.DropSequence(
                name: "cloud_revision_seq");

            migrationBuilder.CreateTable(
                name: "cloud_snapshots",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    hlc_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false)
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
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_sequence = table.Column<long>(type: "bigint", nullable: false),
                    entity_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hlc_logical = table.Column<int>(type: "integer", nullable: false),
                    hlc_physical_ms = table.Column<long>(type: "bigint", nullable: false),
                    operation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_sync_events", x => x.revision);
                });

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
    }
}
