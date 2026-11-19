using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flamme.Persistence.EFCore.PostgreSql.Migrations.Runtime
{
    /// <inheritdoc />
    public partial class V3_2 : Migration
    {
        private readonly Flamme.Persistence.EFCore.IFlammeDbContextSchema _schema;

        /// <inheritdoc />
        public V3_2(Flamme.Persistence.EFCore.IFlammeDbContextSchema schema)
        {
            _schema = schema;
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
