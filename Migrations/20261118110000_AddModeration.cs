using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shoura.API.Data;

#nullable disable

namespace Shoura.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118110000_AddModeration")]
public sealed class AddModeration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var timestampType = ActiveProvider?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true
            ? "TEXT"
            : "timestamp with time zone";
        migrationBuilder.CreateTable(
            name: "UserBlocks",
            columns: table => new
            {
                UserId = table.Column<int>(type: "INTEGER", nullable: false),
                BlockedUserId = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: timestampType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserBlocks", x => new { x.UserId, x.BlockedUserId });
                table.ForeignKey("FK_UserBlocks_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_UserBlocks_Users_BlockedUserId", x => x.BlockedUserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ContentReports",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                ReporterUserId = table.Column<int>(type: "INTEGER", nullable: false),
                TargetUserId = table.Column<int>(type: "INTEGER", nullable: true),
                MessageId = table.Column<int>(type: "INTEGER", nullable: true),
                Category = table.Column<string>(type: "TEXT", nullable: false),
                Details = table.Column<string>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: timestampType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ContentReports", x => x.Id);
                table.ForeignKey("FK_ContentReports_Users_ReporterUserId", x => x.ReporterUserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_ContentReports_Users_TargetUserId", x => x.TargetUserId, "Users", "Id", onDelete: ReferentialAction.SetNull);
                table.ForeignKey("FK_ContentReports_Messages_MessageId", x => x.MessageId, "Messages", "Id", onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex("IX_UserBlocks_BlockedUserId", "UserBlocks", "BlockedUserId");
        migrationBuilder.CreateIndex("IX_ContentReports_ReporterUserId", "ContentReports", "ReporterUserId");
        migrationBuilder.CreateIndex("IX_ContentReports_TargetUserId", "ContentReports", "TargetUserId");
        migrationBuilder.CreateIndex("IX_ContentReports_MessageId", "ContentReports", "MessageId");
        migrationBuilder.CreateIndex("IX_ContentReports_Status_CreatedAtUtc", "ContentReports", new[] { "Status", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ContentReports");
        migrationBuilder.DropTable("UserBlocks");
    }
}
