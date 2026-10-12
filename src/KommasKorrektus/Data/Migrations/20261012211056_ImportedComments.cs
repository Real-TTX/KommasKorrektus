using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KommasKorrektus.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportedComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "DocumentComment",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WrittenDate",
                table: "DocumentComment",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "DocumentComment");

            migrationBuilder.DropColumn(
                name: "WrittenDate",
                table: "DocumentComment");
        }
    }
}
