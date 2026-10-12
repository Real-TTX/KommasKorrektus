using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KommasKorrektus.Data.Migrations
{
    /// <inheritdoc />
    public partial class PageIsStandard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EditorOptions",
                table: "User",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            // The page is the standard: for everybody who is there, and for whoever comes (the default of the columns).
            migrationBuilder.Sql("UPDATE \"User\" SET \"EditorView\" = 'page', \"CompareView\" = 'pages'");
            migrationBuilder.Sql("ALTER TABLE \"User\" ALTER COLUMN \"EditorView\" SET DEFAULT 'page'");
            migrationBuilder.Sql("ALTER TABLE \"User\" ALTER COLUMN \"CompareView\" SET DEFAULT 'pages'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"User\" ALTER COLUMN \"EditorView\" SET DEFAULT 'text'");
            migrationBuilder.Sql("ALTER TABLE \"User\" ALTER COLUMN \"CompareView\" SET DEFAULT 'side'");
            migrationBuilder.DropColumn(
                name: "EditorOptions",
                table: "User");
        }
    }
}
