using Microsoft.EntityFrameworkCore;
using SpaceSpreadsheetEmulator.Persistence.Entities;

namespace SpaceSpreadsheetEmulator.Persistence.Database;

internal sealed class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();

    public DbSet<CharacterEntity> Characters => Set<CharacterEntity>();

    public DbSet<InventoryItemEntity> Items => Set<InventoryItemEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("account_ids", "identity")
            .StartsAt(1)
            .IncrementsBy(10);
        modelBuilder.HasSequence<long>("character_ids", "characters")
            .StartsAt(90_000_001)
            .IncrementsBy(10);
        modelBuilder.HasSequence<long>("item_ids", "inventory")
            .StartsAt(190_000_001)
            .IncrementsBy(10);

        ConfigureAccounts(modelBuilder);
        ConfigureCharacters(modelBuilder);
        ConfigureItems(modelBuilder);
    }

    private static void ConfigureAccounts(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<AccountEntity>();
        account.ToTable("accounts", "identity", table =>
        {
            table.HasCheckConstraint("ck_accounts_account_id_positive", "account_id > 0");
            table.HasCheckConstraint("ck_accounts_version_positive", "version > 0");
        });
        account.HasKey(entity => entity.AccountId);
        account.Property(entity => entity.AccountId)
            .HasColumnName("account_id")
            .UseHiLo("account_ids", "identity");
        account.Property(entity => entity.UserName)
            .HasColumnName("user_name")
            .HasMaxLength(100)
            .IsRequired();
        account.Property(entity => entity.NormalizedUserName)
            .HasColumnName("normalized_user_name")
            .HasMaxLength(100)
            .IsRequired();
        account.Property(entity => entity.CreatedAt)
            .HasColumnName("created_at");
        account.Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at");
        account.Property(entity => entity.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();
        account.HasIndex(entity => entity.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("ux_accounts_normalized_user_name");
    }

    private static void ConfigureCharacters(ModelBuilder modelBuilder)
    {
        var character = modelBuilder.Entity<CharacterEntity>();
        character.ToTable("characters", "characters", table =>
        {
            table.HasCheckConstraint("ck_characters_character_id_positive", "character_id > 0");
            table.HasCheckConstraint("ck_characters_account_id_positive", "account_id > 0");
            table.HasCheckConstraint("ck_characters_static_ids_positive",
                "race_id > 0 AND bloodline_id > 0 AND ancestry_id > 0 "
                + "AND character_type_id > 0 AND corporation_id > 0");
            table.HasCheckConstraint("ck_characters_location_ids_positive",
                "station_id > 0 AND solar_system_id > 0 AND constellation_id > 0 AND region_id > 0");
            table.HasCheckConstraint("ck_characters_active_ship_positive", "active_ship_item_id > 0");
            table.HasCheckConstraint("ck_characters_version_positive", "version > 0");
        });
        character.HasKey(entity => entity.CharacterId);
        character.Property(entity => entity.CharacterId)
            .HasColumnName("character_id")
            .UseHiLo("character_ids", "characters");
        character.Property(entity => entity.AccountId).HasColumnName("account_id");
        character.Property(entity => entity.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();
        character.Property(entity => entity.RaceId).HasColumnName("race_id");
        character.Property(entity => entity.BloodlineId).HasColumnName("bloodline_id");
        character.Property(entity => entity.AncestryId).HasColumnName("ancestry_id");
        character.Property(entity => entity.CharacterTypeId).HasColumnName("character_type_id");
        character.Property(entity => entity.CorporationId).HasColumnName("corporation_id");
        character.Property(entity => entity.StationId).HasColumnName("station_id");
        character.Property(entity => entity.SolarSystemId).HasColumnName("solar_system_id");
        character.Property(entity => entity.ConstellationId).HasColumnName("constellation_id");
        character.Property(entity => entity.RegionId).HasColumnName("region_id");
        character.Property(entity => entity.ActiveShipItemId).HasColumnName("active_ship_item_id");
        character.Property(entity => entity.LastLoginAt).HasColumnName("last_login_at");
        character.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        character.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        character.Property(entity => entity.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();
        character.HasIndex(entity => entity.AccountId)
            .IsUnique()
            .HasDatabaseName("ux_characters_account_id");
        character.HasIndex(entity => entity.ActiveShipItemId)
            .IsUnique()
            .HasDatabaseName("ux_characters_active_ship_item_id");
        character.HasOne<AccountEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        character.HasOne<InventoryItemEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.ActiveShipItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureItems(ModelBuilder modelBuilder)
    {
        var item = modelBuilder.Entity<InventoryItemEntity>();
        item.ToTable("items", "inventory", table =>
        {
            table.HasCheckConstraint("ck_items_positive_ids",
                "item_id > 0 AND type_id > 0 AND owner_id > 0 AND location_id > 0");
            table.HasCheckConstraint("ck_items_location_kind", "location_kind IN (1, 2, 3, 4)");
            table.HasCheckConstraint("ck_items_flag", "flag IN (0, 1)");
            table.HasCheckConstraint("ck_items_quantity_positive", "quantity > 0");
            table.HasCheckConstraint("ck_items_singleton_quantity", "NOT singleton OR quantity = 1");
            table.HasCheckConstraint("ck_items_version_positive", "version > 0");
            table.HasCheckConstraint("ck_items_updated_after_created", "updated_at >= created_at");
        });
        item.HasKey(entity => entity.ItemId);
        item.Property(entity => entity.ItemId)
            .HasColumnName("item_id")
            .UseHiLo("item_ids", "inventory");
        item.Property(entity => entity.TypeId).HasColumnName("type_id");
        item.Property(entity => entity.OwnerId).HasColumnName("owner_id");
        item.Property(entity => entity.LocationId).HasColumnName("location_id");
        item.Property(entity => entity.LocationKind).HasColumnName("location_kind");
        item.Property(entity => entity.Flag).HasColumnName("flag");
        item.Property(entity => entity.Quantity).HasColumnName("quantity");
        item.Property(entity => entity.Singleton).HasColumnName("singleton");
        item.Property(entity => entity.CustomName)
            .HasColumnName("custom_name")
            .HasMaxLength(100);
        item.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        item.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        item.Property(entity => entity.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();
        item.HasIndex(entity => new
        {
            entity.OwnerId,
            entity.LocationKind,
            entity.LocationId,
        })
            .HasDatabaseName("ix_items_owner_location");
        item.HasIndex(entity => new
        {
            entity.LocationKind,
            entity.LocationId,
            entity.Flag,
        })
            .HasDatabaseName("ix_items_location_flag");
    }
}
