using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flamme.Persistence.EFCore.PostgreSql.Migrations.Labels
{
    /// <inheritdoc />
    public partial class V3_2 : Migration
    {
        private readonly IFlammeDbContextSchema _schema;

        /// <inheritdoc />
        public V3_2(IFlammeDbContextSchema schema)
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
