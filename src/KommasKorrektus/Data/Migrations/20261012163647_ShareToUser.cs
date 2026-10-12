using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KommasKorrektus.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShareToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "UserId",
                table: "ShareLink",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UserId",
                table: "ShareAccess",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShareLink_UserId",
                table: "ShareLink",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShareLink_User_UserId",
                table: "ShareLink",
                column: "UserId",
                principalTable: "User",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShareLink_User_UserId",
                table: "ShareLink");

            migrationBuilder.DropIndex(
                name: "IX_ShareLink_UserId",
                table: "ShareLink");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ShareLink");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ShareAccess");
        }
    }
}
