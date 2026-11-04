using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoxLine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVesselsAndVoyages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Vessels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ImoNumber = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vessels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Voyages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VesselId = table.Column<int>(type: "int", nullable: false),
                    VoyageNumber = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Voyages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Voyages_Vessels_VesselId",
                        column: x => x.VesselId,
                        principalTable: "Vessels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VoyageCalls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VoyageId = table.Column<int>(type: "int", nullable: false),
                    PortId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Eta = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Etd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Ata = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Atd = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoyageCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoyageCalls_Ports_PortId",
                        column: x => x.PortId,
                        principalTable: "Ports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoyageCalls_Voyages_VoyageId",
                        column: x => x.VoyageId,
                        principalTable: "Voyages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_VoyageId",
                table: "Bookings",
                column: "VoyageId");

            migrationBuilder.CreateIndex(
                name: "IX_Vessels_ImoNumber",
                table: "Vessels",
                column: "ImoNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoyageCalls_PortId",
                table: "VoyageCalls",
                column: "PortId");

            migrationBuilder.CreateIndex(
                name: "IX_VoyageCalls_VoyageId_PortId",
                table: "VoyageCalls",
                columns: new[] { "VoyageId", "PortId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoyageCalls_VoyageId_Sequence",
                table: "VoyageCalls",
                columns: new[] { "VoyageId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Voyages_Status_Id",
                table: "Voyages",
                columns: new[] { "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Voyages_VesselId",
                table: "Voyages",
                column: "VesselId");

            migrationBuilder.CreateIndex(
                name: "IX_Voyages_VoyageNumber",
                table: "Voyages",
                column: "VoyageNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Voyages_VoyageId",
                table: "Bookings",
                column: "VoyageId",
                principalTable: "Voyages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_Voyages_VoyageId",
                table: "Bookings");

            migrationBuilder.DropTable(
                name: "VoyageCalls");

            migrationBuilder.DropTable(
                name: "Voyages");

            migrationBuilder.DropTable(
                name: "Vessels");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_VoyageId",
                table: "Bookings");
        }
    }
}
