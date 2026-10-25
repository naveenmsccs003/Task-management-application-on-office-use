using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Authorization;

namespace TaskManagement.Api.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<MemberPermissionOverride> MemberPermissionOverrides => Set<MemberPermissionOverride>();

    public DbSet<MemberPersonalInformation> MemberPersonalInformation => Set<MemberPersonalInformation>();

    public DbSet<MemberProfilePhoto> MemberProfilePhotos => Set<MemberProfilePhoto>();

    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.DisplayName).HasMaxLength(ApplicationUser.DisplayNameMaxLength);

            // Identity's default email index is not unique; sign-in by email requires uniqueness.
            user.HasIndex(u => u.NormalizedEmail)
                .HasDatabaseName("EmailIndex")
                .IsUnique()
                .HasFilter("[NormalizedEmail] IS NOT NULL");
            user.HasIndex(u => u.IsActive);
        });

        builder.Entity<ApplicationRole>(role =>
        {
            role.Property(r => r.Description).HasMaxLength(ApplicationRole.DescriptionMaxLength);
            role.Property(r => r.IsActive).HasDefaultValue(true).HasSentinel(true);
        });

        builder.Entity<UserSession>(session =>
        {
            session.ToTable("UserSessions");
            session.HasKey(s => s.Id);
            session.Property(s => s.Id).HasMaxLength(64).IsUnicode(false);
            session.Property(s => s.Ticket).IsRequired();
            session.HasIndex(s => s.UserId);
            session.HasIndex(s => s.ExpiresAtUtc);
            session.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Permission>(permission =>
        {
            permission.ToTable("Permissions");
            permission.Property(p => p.Id).ValueGeneratedNever();
            permission.Property(p => p.Key).HasMaxLength(100).IsUnicode(false);
            permission.HasIndex(p => p.Key).IsUnique();
            permission.Property(p => p.DisplayName).HasMaxLength(100);
            permission.Property(p => p.Description).HasMaxLength(250);
            permission.Property(p => p.ModuleKey).HasMaxLength(50).IsUnicode(false);
            permission.Property(p => p.Category).HasMaxLength(100);
            permission.HasIndex(p => p.ModuleKey);
            permission.HasOne<Permission>()
                .WithMany()
                .HasForeignKey(p => p.ParentPermissionId)
                .OnDelete(DeleteBehavior.Restrict);
            permission.HasData(PermissionCatalog.ToEntities());
        });

        builder.Entity<RolePermission>(rolePermission =>
        {
            rolePermission.ToTable("RolePermissions");
            // One row per role/permission pair: duplicates are impossible.
            rolePermission.HasKey(rp => new { rp.RoleId, rp.PermissionId });
            rolePermission.HasIndex(rp => rp.PermissionId);
            rolePermission.HasOne<ApplicationRole>().WithMany().HasForeignKey(rp => rp.RoleId).OnDelete(DeleteBehavior.Cascade);
            rolePermission.HasOne<Permission>().WithMany().HasForeignKey(rp => rp.PermissionId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MemberPermissionOverride>(memberOverride =>
        {
            memberOverride.ToTable("MemberPermissionOverrides");
            memberOverride.HasIndex(o => new { o.UserId, o.PermissionId }).IsUnique();
            memberOverride.HasIndex(o => o.PermissionId);
            memberOverride.Property(o => o.Effect).HasConversion<byte>();
            memberOverride.HasOne<ApplicationUser>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Cascade);
            memberOverride.HasOne<Permission>().WithMany().HasForeignKey(o => o.PermissionId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MemberPersonalInformation>(info =>
        {
            info.ToTable("MemberPersonalInformation");
            info.HasKey(i => i.UserId);
            info.Property(i => i.AddressLine).HasMaxLength(200);
            info.Property(i => i.City).HasMaxLength(100);
            info.Property(i => i.StateOrProvince).HasMaxLength(100);
            info.Property(i => i.PostalCode).HasMaxLength(20);
            info.Property(i => i.Country).HasMaxLength(100);
            info.HasOne<ApplicationUser>().WithOne().HasForeignKey<MemberPersonalInformation>(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MemberProfilePhoto>(photo =>
        {
            photo.ToTable("MemberProfilePhotos");
            photo.HasKey(p => p.UserId);
            photo.Property(p => p.ContentType).HasMaxLength(50).IsUnicode(false);
            photo.HasOne<ApplicationUser>().WithOne().HasForeignKey<MemberProfilePhoto>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AuditLogEntry>(audit =>
        {
            audit.ToTable("AuditLogs");
            audit.Property(a => a.Action).HasMaxLength(64).IsUnicode(false);
            audit.Property(a => a.TargetType).HasMaxLength(32).IsUnicode(false);
            audit.Property(a => a.TargetId).HasMaxLength(64).IsUnicode(false);
            audit.Property(a => a.Summary).HasMaxLength(2000);
            audit.HasIndex(a => new { a.TargetType, a.TargetId, a.TimestampUtc });
            audit.HasIndex(a => a.TimestampUtc);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<ApplicationUser>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAtUtc == default)
                {
                    entry.Entity.CreatedAtUtc = now;
                }
                entry.Entity.UpdatedAtUtc = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ApplicationRole>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAtUtc == default)
            {
                entry.Entity.CreatedAtUtc = now;
            }
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
            }
        }
    }
}
