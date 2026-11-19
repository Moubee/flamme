using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flamme.Persistence.EFCore.PostgreSql.Migrations.Management
{
    /// <inheritdoc />
    public partial class V3_4 : Migration
    {
        private readonly Flamme.Persistence.EFCore.IFlammeDbContextSchema _schema;

        /// <inheritdoc />
        public V3_4(Flamme.Persistence.EFCore.IFlammeDbContextSchema schema)
        {
            _schema = schema;
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExecuting",
                schema: _schema.Schema,
                table: "WorkflowInstances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstance_IsExecuting",
                schema: _schema.Schema,
                table: "WorkflowInstances",
                column: "IsExecuting");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstance_IsExecuting",
                schema: _schema.Schema,
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "IsExecuting",
                schema: _schema.Schema,
                table: "WorkflowInstances");
        }
    }
}
