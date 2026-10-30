using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaskRunner.Core.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TaskConfigs",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                JobId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                JobName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CronExpr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                JobType = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TaskConfigs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TaskConfigs_JobId",
            table: "TaskConfigs",
            column: "JobId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TaskConfigs");
    }
}
