using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KommasKorrektus.Data.Migrations
{
    /// <summary>
    /// The version that is still being worked on was stored as "Autosave"; every save goes into it now, not only the automatic one, so it is "Working".
    /// Enums are stored as text, so the rows have to follow the name. The model itself does not change.
    /// </summary>
    [DbContext(typeof(KkDbContext))]
    [Migration("20261012140000_WorkingVersions")]
    public partial class WorkingVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"DocumentVersion\" SET \"Origin\" = 'Working' WHERE \"Origin\" = 'Autosave';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"DocumentVersion\" SET \"Origin\" = 'Autosave' WHERE \"Origin\" = 'Working';");
        }
    }
}
