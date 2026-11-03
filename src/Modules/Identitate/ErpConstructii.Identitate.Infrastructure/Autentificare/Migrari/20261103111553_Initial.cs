using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ErpConstructii.Identitate.Infrastructure.Autentificare.Migrari
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identitate");

            migrationBuilder.CreateTable(
                name: "conturi",
                schema: "identitate",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ultima_firma_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conturi", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "oidc_aplicatii",
                schema: "identitate",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    client_secret = table.Column<string>(type: "text", nullable: true),
                    client_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    consent_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    json_web_key_set = table.Column<string>(type: "text", nullable: true),
                    permissions = table.Column<string>(type: "text", nullable: true),
                    post_logout_redirect_uris = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redirect_uris = table.Column<string>(type: "text", nullable: true),
                    requirements = table.Column<string>(type: "text", nullable: true),
                    settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oidc_aplicatii", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "oidc_scopuri",
                schema: "identitate",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    descriptions = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oidc_scopuri", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conturi_autentificari_externe",
                schema: "identitate",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conturi_autentificari_externe", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_conturi_autentificari_externe_conturi_user_id",
                        column: x => x.user_id,
                        principalSchema: "identitate",
                        principalTable: "conturi",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conturi_revendicari",
                schema: "identitate",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conturi_revendicari", x => x.id);
                    table.ForeignKey(
                        name: "fk_conturi_revendicari_conturi_user_id",
                        column: x => x.user_id,
                        principalSchema: "identitate",
                        principalTable: "conturi",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conturi_tokenuri",
                schema: "identitate",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conturi_tokenuri", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_conturi_tokenuri_conturi_user_id",
                        column: x => x.user_id,
                        principalSchema: "identitate",
                        principalTable: "conturi",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oidc_autorizari",
                schema: "identitate",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    scopes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oidc_autorizari", x => x.id);
                    table.ForeignKey(
                        name: "fk_oidc_autorizari_oidc_aplicatii_application_id",
                        column: x => x.application_id,
                        principalSchema: "identitate",
                        principalTable: "oidc_aplicatii",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "oidc_tokenuri",
                schema: "identitate",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    authorization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expiration_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payload = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redemption_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reference_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oidc_tokenuri", x => x.id);
                    table.ForeignKey(
                        name: "fk_oidc_tokenuri_oidc_aplicatii_application_id",
                        column: x => x.application_id,
                        principalSchema: "identitate",
                        principalTable: "oidc_aplicatii",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_oidc_tokenuri_oidc_autorizari_authorization_id",
                        column: x => x.authorization_id,
                        principalSchema: "identitate",
                        principalTable: "oidc_autorizari",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identitate",
                table: "conturi",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "identitate",
                table: "conturi",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conturi_autentificari_externe_user_id",
                schema: "identitate",
                table: "conturi_autentificari_externe",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_conturi_revendicari_user_id",
                schema: "identitate",
                table: "conturi_revendicari",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_oidc_aplicatii_client_id",
                schema: "identitate",
                table: "oidc_aplicatii",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_oidc_autorizari_application_id_status_subject_type",
                schema: "identitate",
                table: "oidc_autorizari",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_oidc_scopuri_name",
                schema: "identitate",
                table: "oidc_scopuri",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_oidc_tokenuri_application_id_status_subject_type",
                schema: "identitate",
                table: "oidc_tokenuri",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_oidc_tokenuri_authorization_id",
                schema: "identitate",
                table: "oidc_tokenuri",
                column: "authorization_id");

            migrationBuilder.CreateIndex(
                name: "ix_oidc_tokenuri_reference_id",
                schema: "identitate",
                table: "oidc_tokenuri",
                column: "reference_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conturi_autentificari_externe",
                schema: "identitate");

            migrationBuilder.DropTable(
                name: "conturi_revendicari",
                schema: "identitate");

            migrationBuilder.DropTable(
                name: "conturi_tokenuri",
                schema: "identitate");

            migrationBuilder.DropTable(
                name: "oidc_scopuri",
                schema: "identitate");

            migrationBuilder.DropTable(
                name: "oidc_tokenuri",
                schema: "identitate");

            migrationBuilder.DropTable(
                name: "conturi",
                schema: "identitate");

            migrationBuilder.DropTable(
                name: "oidc_autorizari",
                schema: "identitate");

            migrationBuilder.DropTable(
                name: "oidc_aplicatii",
                schema: "identitate");
        }
    }
}
