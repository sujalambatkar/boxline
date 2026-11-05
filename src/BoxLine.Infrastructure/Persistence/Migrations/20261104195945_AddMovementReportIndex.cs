using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoxLine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMovementReportIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "OccurredMonth",
                table: "ContainerMovements",
                type: "date",
                nullable: false,
                computedColumnSql: "DATEFROMPARTS(YEAR([OccurredAt]), MONTH([OccurredAt]), 1)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContainerMovements_Report",
                table: "ContainerMovements",
                columns: new[] { "OccurredMonth", "LocationUnLocode", "ContainerId" })
                .Annotation("SqlServer:Include", new[] { "EventType" });

            // Utilisation by month and location, computed in the database and paged there. The filtered statements use
            // OPTION (RECOMPILE) so each combination of date range and location gets a plan that suits it, rather than one
            // sniffed from whichever call happened to run first (the "optional parameter" pattern is the classic trap).
            migrationBuilder.Sql(
                """
                CREATE OR ALTER PROCEDURE dbo.usp_UtilisationByMonthAndPort
                    @FromMonth date = NULL,
                    @ToMonth date = NULL,
                    @Location char(5) = NULL,
                    @Page int = 1,
                    @PageSize int = 20
                AS
                BEGIN
                    SET NOCOUNT ON;

                    SELECT m.OccurredMonth AS [Month],
                           m.LocationUnLocode AS Location,
                           COUNT(*) AS Movements,
                           COUNT(DISTINCT m.ContainerId) AS Containers,
                           SUM(CASE WHEN m.EventType = 'LoadedOnVessel' THEN 1 ELSE 0 END) AS Loaded,
                           SUM(CASE WHEN m.EventType = 'Discharged' THEN 1 ELSE 0 END) AS Discharged
                    INTO #Grouped
                    FROM dbo.ContainerMovements AS m
                    WHERE (@FromMonth IS NULL OR m.OccurredMonth >= @FromMonth)
                      AND (@ToMonth IS NULL OR m.OccurredMonth <= @ToMonth)
                      AND (@Location IS NULL OR m.LocationUnLocode = @Location)
                    GROUP BY m.OccurredMonth, m.LocationUnLocode
                    OPTION (RECOMPILE);

                    SELECT (SELECT COUNT(*) FROM #Grouped) AS TotalRows,
                           (SELECT COUNT(*) FROM dbo.Containers) AS FleetSize;

                    SELECT g.[Month], g.Location, p.Name AS PortName, g.Movements, g.Containers, g.Loaded, g.Discharged
                    FROM #Grouped AS g
                    LEFT JOIN dbo.Ports AS p ON p.UnLocode = g.Location
                    ORDER BY g.[Month] DESC, g.Location COLLATE Latin1_General_BIN2
                    OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;

                    SELECT m.OccurredMonth AS [Month],
                           COUNT(*) AS Movements,
                           COUNT(DISTINCT m.ContainerId) AS ActiveContainers
                    FROM dbo.ContainerMovements AS m
                    WHERE (@FromMonth IS NULL OR m.OccurredMonth >= @FromMonth)
                      AND (@ToMonth IS NULL OR m.OccurredMonth <= @ToMonth)
                      AND (@Location IS NULL OR m.LocationUnLocode = @Location)
                    GROUP BY m.OccurredMonth
                    ORDER BY m.OccurredMonth
                    OPTION (RECOMPILE);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_UtilisationByMonthAndPort;");

            migrationBuilder.DropIndex(
                name: "IX_ContainerMovements_Report",
                table: "ContainerMovements");

            migrationBuilder.DropColumn(
                name: "OccurredMonth",
                table: "ContainerMovements");
        }
    }
}
