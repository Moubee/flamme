using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flamme.Persistence.EFCore.PostgreSql.Migrations.Identity
{
    /// <inheritdoc />
    public partial class RevokedSessions : Migration
    {
        private readonly Flamme.Persistence.EFCore.IFlammeDbContextSchema _schema;

        /// <inheritdoc />
        public RevokedSessions(Flamme.Persistence.EFCore.IFlammeDbContextSchema schema)
        {
            _schema = schema;
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RevokedSessions",
                schema: _schema.Schema,
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevokedSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RevokedSession_ExpiresAt",
                schema: _schema.Schema,
                table: "RevokedSessions",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RevokedSessions",
                schema: _schema.Schema);
        }
    }
}
