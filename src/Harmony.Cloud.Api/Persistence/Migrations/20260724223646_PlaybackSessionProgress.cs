using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Harmony.Cloud.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlaybackSessionProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "current_song_id",
                table: "cloud_playback_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "duration_ms",
                table: "cloud_playback_sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "playing",
                table: "cloud_playback_sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "position_ms",
                table: "cloud_playback_sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "progress_updated_at",
                table: "cloud_playback_sessions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "current_song_id",
                table: "cloud_playback_sessions");

            migrationBuilder.DropColumn(
                name: "duration_ms",
                table: "cloud_playback_sessions");

            migrationBuilder.DropColumn(
                name: "playing",
                table: "cloud_playback_sessions");

            migrationBuilder.DropColumn(
                name: "position_ms",
                table: "cloud_playback_sessions");

            migrationBuilder.DropColumn(
                name: "progress_updated_at",
                table: "cloud_playback_sessions");
        }
    }
}
