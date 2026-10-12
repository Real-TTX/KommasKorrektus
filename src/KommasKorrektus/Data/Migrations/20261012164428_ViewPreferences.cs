using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KommasKorrektus.Data.Migrations
{
    /// <inheritdoc />
    public partial class ViewPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompareView",
                table: "User",
                type: "text",
                nullable: false,
                defaultValue: "side");

            migrationBuilder.AddColumn<string>(
                name: "EditorView",
                table: "User",
                type: "text",
                nullable: false,
                defaultValue: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompareView",
                table: "User");

            migrationBuilder.DropColumn(
                name: "EditorView",
                table: "User");
        }
    }
}
