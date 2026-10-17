using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MesDeviceAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthMethod",
                schema: "qms",
                table: "mes_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "UserToken");

            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                schema: "qms",
                table: "mes_events",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "AllowedEndpoints",
                schema: "qms",
                table: "api_clients",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<int>(
                name: "FailureCount",
                schema: "qms",
                table: "api_clients",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                schema: "qms",
                table: "api_clients",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastFailureAt",
                schema: "qms",
                table: "api_clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastFailureReason",
                schema: "qms",
                table: "api_clients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastUsedFrom",
                schema: "qms",
                table: "api_clients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousSecretHash",
                schema: "qms",
                table: "api_clients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PreviousSecretValidUntil",
                schema: "qms",
                table: "api_clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                schema: "qms",
                table: "api_clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RevokedById",
                schema: "qms",
                table: "api_clients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevokedReason",
                schema: "qms",
                table: "api_clients",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RotatedAt",
                schema: "qms",
                table: "api_clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "Scopes",
                schema: "qms",
                table: "api_clients",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "SiteIds",
                schema: "qms",
                table: "api_clients",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.CreateIndex(
                name: "IX_mes_events_AuthMethod_ReceivedAt",
                schema: "qms",
                table: "mes_events",
                columns: new[] { "AuthMethod", "ReceivedAt" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_mes_events_AuthMethod_ReceivedAt",
                schema: "qms",
                table: "mes_events");

            migrationBuilder.DropColumn(
                name: "AuthMethod",
                schema: "qms",
                table: "mes_events");

            migrationBuilder.DropColumn(
                name: "ClientId",
                schema: "qms",
                table: "mes_events");

            migrationBuilder.DropColumn(
                name: "AllowedEndpoints",
                schema: "qms",
                table: "api_clients");

            migrationBuilder.DropColumn(
                name: "FailureCount",
                schema: "qms",
                table: "api_clients");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "qms",
                table: "api_clients");

            migrationBuilder.DropColumn(
                name: "LastFailureAt",
                schema: "qms",
                table: "api_clients");

            migrationBuilder.DropColumn(
                name: "LastFailureReason",
                schema: "qms",
                table: "api_clients");

            migrationBuilder.DropColumn(
                name: "LastUsedFrom",
                schema: "qms",
                table: "api_clients");

            migrationBuilder.DropColumn(
                name: "PreviousSecretHash",
                schema: "qms",
                table: "api_clients");

            migrationBuilder.DropColumn(
                name: "PreviousSecretValidUntil",
                schema: "qms",
                table: "api_clients");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                schema: "qms",
                table: "api_clients");

            migrationBuilder.DropColumn(
                name: "RevokedById",
                schema: "qms",
                table: "api_clients");

            migrationBuilder.DropColumn(
                name: "RevokedReason",
                schema: "qms",
                table: "api_clients");

            migrationBuilder.DropColumn(
                name: "RotatedAt",
                schema: "qms",
                table: "api_clients");

            migrationBuilder.DropColumn(
                name: "Scopes",
                schema: "qms",
                table: "api_clients");

            migrationBuilder.DropColumn(
                name: "SiteIds",
                schema: "qms",
                table: "api_clients");
        }
    }
}
