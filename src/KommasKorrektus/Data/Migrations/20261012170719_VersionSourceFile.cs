using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KommasKorrektus.Data.Migrations
{
    /// <inheritdoc />
    public partial class VersionSourceFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SourceFileId",
                table: "DocumentVersion",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersion_SourceFileId",
                table: "DocumentVersion",
                column: "SourceFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentVersion_StoredFile_SourceFileId",
                table: "DocumentVersion",
                column: "SourceFileId",
                principalTable: "StoredFile",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentVersion_StoredFile_SourceFileId",
                table: "DocumentVersion");

            migrationBuilder.DropIndex(
                name: "IX_DocumentVersion_SourceFileId",
                table: "DocumentVersion");

            migrationBuilder.DropColumn(
                name: "SourceFileId",
                table: "DocumentVersion");
        }
    }
}
