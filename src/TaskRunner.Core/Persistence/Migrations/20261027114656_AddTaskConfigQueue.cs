using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskRunner.Core.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTaskConfigQueue : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Queue",
            table: "TaskConfigs",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "default");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Queue",
            table: "TaskConfigs");
    }
}
