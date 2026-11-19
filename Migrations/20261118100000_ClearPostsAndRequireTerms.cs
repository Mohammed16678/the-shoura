using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shoura.API.Data;

#nullable disable

namespace Shoura.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118100000_ClearPostsAndRequireTerms")]
public sealed class ClearPostsAndRequireTerms : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "AcceptedTermsVersion", table: "Users", type: "TEXT", nullable: true);
        migrationBuilder.Sql("DELETE FROM \"Posts\";");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AcceptedTermsVersion", table: "Users");
    }
}
