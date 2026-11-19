using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shoura.API.Data;

namespace Shoura.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261119160000_AddPostHashtags")]
public sealed class AddPostHashtags : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "Hashtags", table: "Posts", type: "TEXT", nullable: false, defaultValue: "");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Hashtags", table: "Posts");
}
