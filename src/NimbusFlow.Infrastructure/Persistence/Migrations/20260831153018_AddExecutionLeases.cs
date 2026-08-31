using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NimbusFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExecutionLeaseExpiresAt",
                table: "WorkflowJobs",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutionLeaseExpiresAt",
                table: "WorkflowJobs");
        }
    }
}
