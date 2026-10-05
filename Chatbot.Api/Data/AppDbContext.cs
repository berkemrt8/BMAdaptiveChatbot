using Chatbot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Chatbot.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<BotProfile> BotProfiles => Set<BotProfile>();
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<HelpCategory> HelpCategories => Set<HelpCategory>();
    public DbSet<HelpCategoryAlias> HelpCategoryAliases => Set<HelpCategoryAlias>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<MenuItemAlias> MenuItemAliases => Set<MenuItemAlias>();
    public DbSet<FollowUpPhrase> FollowUpPhrases => Set<FollowUpPhrase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BotProfile>()
            .HasIndex(x => x.PublicKey)
            .IsUnique();

        modelBuilder.Entity<KnowledgeArticle>()
            .HasIndex(x => new { x.BotProfileId, x.IntentName })
            .IsUnique();

        modelBuilder.Entity<KnowledgeArticle>()
            .HasOne(x => x.BotProfile)
            .WithMany(x => x.KnowledgeArticles)
            .HasForeignKey(x => x.BotProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ConversationMessage>()
            .HasOne(x => x.BotProfile)
            .WithMany(x => x.ConversationMessages)
            .HasForeignKey(x => x.BotProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        // Menü: bot silinirse kategorileri, maddeleri ve ifadeleri de silinir.
        modelBuilder.Entity<HelpCategory>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Icon).HasMaxLength(20);
            entity.Property(x => x.Prompt).HasMaxLength(500);
            entity.HasOne(x => x.BotProfile)
                .WithMany(x => x.HelpCategories)
                .HasForeignKey(x => x.BotProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HelpCategoryAlias>(entity =>
        {
            entity.Property(x => x.Phrase).HasMaxLength(200);
            entity.HasOne(x => x.HelpCategory)
                .WithMany(x => x.Aliases)
                .HasForeignKey(x => x.HelpCategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MenuItem>(entity =>
        {
            entity.Property(x => x.IntentName).HasMaxLength(100);
            entity.Property(x => x.Title).HasMaxLength(300);
            entity.Property(x => x.ActionText).HasMaxLength(200);
            entity.Property(x => x.ActionUrl).HasMaxLength(500);
            entity.HasOne(x => x.HelpCategory)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.HelpCategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MenuItemAlias>(entity =>
        {
            entity.Property(x => x.Phrase).HasMaxLength(200);
            entity.HasOne(x => x.MenuItem)
                .WithMany(x => x.Aliases)
                .HasForeignKey(x => x.MenuItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FollowUpPhrase>(entity =>
        {
            entity.Property(x => x.Phrase).HasMaxLength(100);
            entity.HasOne(x => x.BotProfile)
                .WithMany(x => x.FollowUpPhrases)
                .HasForeignKey(x => x.BotProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
