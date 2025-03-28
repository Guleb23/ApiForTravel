using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiForTravel.Migrations
{
    /// <inheritdoc />
    public partial class likes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LikesCount",
                table: "Travels",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LikesCount",
                table: "Travels");
        }
    }
}
