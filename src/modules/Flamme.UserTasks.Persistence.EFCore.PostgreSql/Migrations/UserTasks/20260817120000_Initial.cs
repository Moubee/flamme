using Flamme.Persistence.EFCore;
using Flamme.UserTasks.Persistence.EFCore;
using Flamme.UserTasks.Persistence.EFCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Flamme.UserTasks.Persistence.EFCore.PostgreSql.Migrations.UserTasks;

[DbContext(typeof(UserTasksFlammeDbContext))]
[Migration("20260817120000_Initial")]
public partial class Initial : Migration
{
    private readonly IFlammeDbContextSchema schema;
    public Initial(IFlammeDbContextSchema schema) => this.schema = schema ?? throw new ArgumentNullException(nameof(schema));
    protected override void Up(MigrationBuilder migrationBuilder) => UserTasksSchemaMigration.Up(migrationBuilder, schema.Schema);
    protected override void Down(MigrationBuilder migrationBuilder) => UserTasksSchemaMigration.Down(migrationBuilder, schema.Schema);
}
