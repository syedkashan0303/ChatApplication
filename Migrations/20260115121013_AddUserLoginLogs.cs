using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignalRMVC.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLoginLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserLoginLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LoginDateTime = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "GETDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLoginLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserLoginLogs_UserId_LoginDateTime",
                table: "UserLoginLogs",
                columns: new[] { "UserId", "LoginDateTime" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserLoginLogs");
        }
    }
}
