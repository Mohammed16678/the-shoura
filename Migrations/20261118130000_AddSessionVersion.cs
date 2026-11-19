using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shoura.API.Data;

#nullable disable

namespace Shoura.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118130000_AddSessionVersion")]
public sealed class AddSessionVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<int>(name: "SessionVersion", table: "Users", type: "INTEGER", nullable: false, defaultValue: 0);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "SessionVersion", table: "Users");
}
