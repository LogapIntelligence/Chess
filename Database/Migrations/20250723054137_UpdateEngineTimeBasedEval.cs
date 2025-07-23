using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class UpdateEngineTimeBasedEval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Depth",
                table: "Batches",
                newName: "MovetimeMs");

            migrationBuilder.AddColumn<int>(
                name: "Depth",
                table: "ChessMoves",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Depth",
                table: "ChessMoves");

            migrationBuilder.RenameColumn(
                name: "MovetimeMs",
                table: "Batches",
                newName: "Depth");
        }
    }
}
