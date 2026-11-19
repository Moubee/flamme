using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flamme.Persistence.EFCore.PostgreSql.Migrations.Runtime
{
    /// <inheritdoc />
    public partial class V3_7 : Migration
    {
        private readonly Flamme.Persistence.EFCore.IFlammeDbContextSchema _schema;

        /// <inheritdoc />
        public V3_7(Flamme.Persistence.EFCore.IFlammeDbContextSchema schema)
        {
            _schema = schema;
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CallStackDepth",
                schema: _schema.Schema,
                table: "ActivityExecutionRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchedulingActivityExecutionId",
                schema: _schema.Schema,
                table: "ActivityExecutionRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchedulingActivityId",
                schema: _schema.Schema,
                table: "ActivityExecutionRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchedulingWorkflowInstanceId",
                schema: _schema.Schema,
                table: "ActivityExecutionRecords",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityExecutionRecord_SchedulingActivityExecutionId",
                schema: _schema.Schema,
                table: "ActivityExecutionRecords",
                column: "SchedulingActivityExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityExecutionRecord_SchedulingActivityId",
                schema: _schema.Schema,
                table: "ActivityExecutionRecords",
                column: "SchedulingActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityExecutionRecord_SchedulingWorkflowInstanceId",
                schema: _schema.Schema,
                table: "ActivityExecutionRecords",
                column: "SchedulingWorkflowInstanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityExecutionRecord_SchedulingActivityExecutionId",
                schema: _schema.Schema,
                table: "ActivityExecutionRecords");

            migrationBuilder.DropIndex(
                name: "IX_ActivityExecutionRecord_SchedulingActivityId",
                schema: _schema.Schema,
                table: "ActivityExecutionRecords");

            migrationBuilder.DropIndex(
                name: "IX_ActivityExecutionRecord_SchedulingWorkflowInstanceId",
                schema: _schema.Schema,
                table: "ActivityExecutionRecords");

            migrationBuilder.DropColumn(
                name: "CallStackDepth",
                schema: _schema.Schema,
                table: "ActivityExecutionRecords");

            migrationBuilder.DropColumn(
                name: "SchedulingActivityExecutionId",
                schema: _schema.Schema,
                table: "ActivityExecutionRecords");

            migrationBuilder.DropColumn(
                name: "SchedulingActivityId",
                schema: _schema.Schema,
                table: "ActivityExecutionRecords");

            migrationBuilder.DropColumn(
                name: "SchedulingWorkflowInstanceId",
                schema: _schema.Schema,
                table: "ActivityExecutionRecords");
        }
    }
}
