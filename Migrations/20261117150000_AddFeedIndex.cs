using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shoura.API.Data;

#nullable disable

namespace Shoura.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261117150000_AddFeedIndex")]
public partial class AddFeedIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex("IX_Posts_CreatedAt_Id", "Posts", new[] { "CreatedAt", "Id" });

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropIndex("IX_Posts_CreatedAt_Id", "Posts");
}
