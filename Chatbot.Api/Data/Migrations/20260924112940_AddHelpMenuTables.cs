using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chatbot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHelpMenuTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MenuPrompt",
                table: "BotProfiles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "FollowUpPhrases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BotProfileId = table.Column<int>(type: "int", nullable: false),
                    Phrase = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUpPhrases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FollowUpPhrases_BotProfiles_BotProfileId",
                        column: x => x.BotProfileId,
                        principalTable: "BotProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HelpCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BotProfileId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HelpCategories_BotProfiles_BotProfileId",
                        column: x => x.BotProfileId,
                        principalTable: "BotProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HelpCategoryAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HelpCategoryId = table.Column<int>(type: "int", nullable: false),
                    Phrase = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpCategoryAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HelpCategoryAliases_HelpCategories_HelpCategoryId",
                        column: x => x.HelpCategoryId,
                        principalTable: "HelpCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MenuItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HelpCategoryId = table.Column<int>(type: "int", nullable: false),
                    IntentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ActionText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ActionUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuItems_HelpCategories_HelpCategoryId",
                        column: x => x.HelpCategoryId,
                        principalTable: "HelpCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MenuItemAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MenuItemId = table.Column<int>(type: "int", nullable: false),
                    Phrase = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItemAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuItemAliases_MenuItems_MenuItemId",
                        column: x => x.MenuItemId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPhrases_BotProfileId",
                table: "FollowUpPhrases",
                column: "BotProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_HelpCategories_BotProfileId",
                table: "HelpCategories",
                column: "BotProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_HelpCategoryAliases_HelpCategoryId",
                table: "HelpCategoryAliases",
                column: "HelpCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemAliases_MenuItemId",
                table: "MenuItemAliases",
                column: "MenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_HelpCategoryId",
                table: "MenuItems",
                column: "HelpCategoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FollowUpPhrases");

            migrationBuilder.DropTable(
                name: "HelpCategoryAliases");

            migrationBuilder.DropTable(
                name: "MenuItemAliases");

            migrationBuilder.DropTable(
                name: "MenuItems");

            migrationBuilder.DropTable(
                name: "HelpCategories");

            migrationBuilder.DropColumn(
                name: "MenuPrompt",
                table: "BotProfiles");
        }
    }
}
