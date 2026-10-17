using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualGuardIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MessagingProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_templates",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PushTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MaxLength = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "messaging_subscriptions",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OptedInAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OptedOutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messaging_subscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "messaging_providers",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Adapter = table.Column<int>(type: "integer", nullable: false),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Sender = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SecretReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EncryptedSecret = table.Column<string>(type: "text", nullable: true),
                    EncryptedCallbackToken = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    RateLimitPerMinute = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    RequireOptIn = table.Column<bool>(type: "boolean", nullable: false),
                    Keywords = table.Column<string>(type: "jsonb", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfigFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastTestAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTestOk = table.Column<bool>(type: "boolean", nullable: true),
                    LastTestMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastTestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastTestById = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductionApprovedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductionApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastNotificationId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messaging_providers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_messaging_providers_message_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "qms",
                        principalTable: "message_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "message_deliveries",
                schema: "qms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaskedAddress = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsTest = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_message_deliveries_messaging_providers_ProviderId",
                        column: x => x.ProviderId,
                        principalSchema: "qms",
                        principalTable: "messaging_providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_message_deliveries_ProviderId_NotificationId_SubscriptionId",
                schema: "qms",
                table: "message_deliveries",
                columns: new[] { "ProviderId", "NotificationId", "SubscriptionId" },
                unique: true,
                filter: "\"NotificationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_message_deliveries_ProviderId_ProviderMessageId",
                schema: "qms",
                table: "message_deliveries",
                columns: new[] { "ProviderId", "ProviderMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_message_deliveries_ProviderId_SentAt",
                schema: "qms",
                table: "message_deliveries",
                columns: new[] { "ProviderId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_message_deliveries_State_NextAttemptAt",
                schema: "qms",
                table: "message_deliveries",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_message_templates_Name",
                schema: "qms",
                table: "message_templates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messaging_providers_Name",
                schema: "qms",
                table: "messaging_providers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messaging_providers_TemplateId",
                schema: "qms",
                table: "messaging_providers",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_messaging_subscriptions_UserId_Channel_Address",
                schema: "qms",
                table: "messaging_subscriptions",
                columns: new[] { "UserId", "Channel", "Address" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_deliveries",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "messaging_subscriptions",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "messaging_providers",
                schema: "qms");

            migrationBuilder.DropTable(
                name: "message_templates",
                schema: "qms");
        }
    }
}
