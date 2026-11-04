using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoxLine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDetentionTariffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DetentionTariffs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContainerTypeId = table.Column<int>(type: "int", nullable: false),
                    FreeDays = table.Column<int>(type: "int", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DetentionTariffs", x => x.Id);
                    table.CheckConstraint("CK_DetentionTariffs_FreeDays", "[FreeDays] >= 0");
                    table.ForeignKey(
                        name: "FK_DetentionTariffs_ContainerTypes_ContainerTypeId",
                        column: x => x.ContainerTypeId,
                        principalTable: "ContainerTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DetentionSlabs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TariffId = table.Column<int>(type: "int", nullable: false),
                    FromDay = table.Column<int>(type: "int", nullable: false),
                    ToDay = table.Column<int>(type: "int", nullable: true),
                    RatePerDayUsd = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DetentionSlabs", x => x.Id);
                    table.CheckConstraint("CK_DetentionSlabs_Days", "[FromDay] >= 1 AND ([ToDay] IS NULL OR [ToDay] >= [FromDay])");
                    table.CheckConstraint("CK_DetentionSlabs_Rate", "[RatePerDayUsd] > 0");
                    table.ForeignKey(
                        name: "FK_DetentionSlabs_DetentionTariffs_TariffId",
                        column: x => x.TariffId,
                        principalTable: "DetentionTariffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DetentionSlabs_TariffId_FromDay",
                table: "DetentionSlabs",
                columns: new[] { "TariffId", "FromDay" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DetentionTariffs_ContainerTypeId_ValidFrom",
                table: "DetentionTariffs",
                columns: new[] { "ContainerTypeId", "ValidFrom" });

            // The same calculation as DetentionCalculator (BoxLine.Core). Integration tests prove the two agree,
            // so change them together. Weekends are detected from a fixed reference Saturday (1900-01-06), which
            // keeps the result independent of DATEFIRST and language settings.
            migrationBuilder.Sql(
                """
                CREATE OR ALTER PROCEDURE dbo.usp_CalculateDetention
                    @TariffId int,
                    @OutDate date,
                    @ReturnDate date,
                    @CountWeekends bit
                AS
                BEGIN
                    SET NOCOUNT ON;

                    IF @ReturnDate < @OutDate
                        THROW 50010, 'Return date cannot be before the out date.', 1;

                    DECLARE @FreeDays int, @ValidFrom date, @ValidTo date;

                    SELECT @FreeDays = FreeDays, @ValidFrom = ValidFrom, @ValidTo = ValidTo
                    FROM dbo.DetentionTariffs
                    WHERE Id = @TariffId;

                    IF @@ROWCOUNT = 0
                        THROW 50012, 'Tariff was not found.', 1;

                    IF @OutDate < @ValidFrom OR (@ValidTo IS NOT NULL AND @OutDate > @ValidTo)
                    BEGIN
                        DECLARE @NotValid nvarchar(200) = CONCAT('The tariff is not valid on ', CONVERT(char(10), @OutDate, 23), '.');
                        THROW 50011, @NotValid, 1;
                    END;

                    DECLARE @Span int = DATEDIFF(day, @OutDate, @ReturnDate) + 1;

                    -- One row per day that counts, numbered 1, 2, 3, ... (weekends are skipped when not chargeable).
                    CREATE TABLE #Counted (DayNo int NOT NULL PRIMARY KEY);

                    WITH Numbers AS (
                        SELECT TOP (@Span) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n
                        FROM sys.all_objects a CROSS JOIN sys.all_objects b
                    ),
                    Dated AS (
                        SELECT DATEADD(day, n, @OutDate) AS d FROM Numbers
                    ),
                    Kept AS (
                        SELECT d FROM Dated
                        WHERE @CountWeekends = 1 OR (DATEDIFF(day, CAST('19000106' AS date), d) % 7) NOT IN (0, 1)
                    )
                    INSERT INTO #Counted (DayNo)
                    SELECT ROW_NUMBER() OVER (ORDER BY d) FROM Kept;

                    SELECT s.FromDay, s.ToDay, COUNT(*) AS Days, s.RatePerDayUsd,
                           CAST(COUNT(*) * s.RatePerDayUsd AS decimal(18, 2)) AS AmountUsd
                    INTO #Charges
                    FROM #Counted c
                    JOIN dbo.DetentionSlabs s
                        ON s.TariffId = @TariffId
                       AND c.DayNo >= s.FromDay
                       AND (s.ToDay IS NULL OR c.DayNo <= s.ToDay)
                    WHERE c.DayNo > @FreeDays
                    GROUP BY s.FromDay, s.ToDay, s.RatePerDayUsd;

                    DECLARE @Counted int = (SELECT COUNT(*) FROM #Counted);
                    DECLARE @FreeUsed int = (SELECT COUNT(*) FROM #Counted WHERE DayNo <= @FreeDays);

                    SELECT @Span AS TotalDays,
                           @Counted AS CountedDays,
                           @Span - @Counted AS WeekendDaysSkipped,
                           @FreeUsed AS FreeDaysUsed,
                           @Counted - @FreeUsed AS ChargeableDays,
                           CAST(ISNULL((SELECT SUM(AmountUsd) FROM #Charges), 0) AS decimal(18, 2)) AS TotalAmountUsd;

                    SELECT FromDay, ToDay, Days, RatePerDayUsd, AmountUsd
                    FROM #Charges
                    ORDER BY FromDay;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_CalculateDetention;");

            migrationBuilder.DropTable(
                name: "DetentionSlabs");

            migrationBuilder.DropTable(
                name: "DetentionTariffs");
        }
    }
}
