using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Origin.Domain.Entities;
using Origin.Persistance.Configurations;
using System.Text.Json;
using System.Threading;

namespace Origin.Persistance.Contexts;

public class UserContext : DbContext
{
    public DbSet<TestEntity> TestEntities { get; set; }
    public DbSet<ActionLog> ActionLogs { get; set; }


    public UserContext(DbContextOptions<UserContext> options) : base(options)
    {}


    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
    }


    public override int SaveChanges()
    {
        var notAddedEntries = ChangeTracker.Entries().Where(en => en.State != EntityState.Added).ToList();
        var addedEntries = ChangeTracker.Entries().Where(en => en.State == EntityState.Added).ToList();
        if (notAddedEntries.Count > 0)
        {
            var actionLogs = new List<ActionLog>();
            foreach (var entry in notAddedEntries)
                if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
                    actionLogs.AddRange(CreateAuditLogs(entry, entry.State));
            ActionLogs.AddRange(actionLogs);
        }
        if (addedEntries.Count > 0)
        {
            var entriesWithStates = addedEntries.Select(en => new
            {
                Entry = en,
                en.State
            }).ToList();

            base.SaveChanges();

            var actionLogs = new List<ActionLog>();
            foreach (var entryWithState in entriesWithStates)
                if (entryWithState.State == EntityState.Added || entryWithState.State == EntityState.Modified || entryWithState.State == EntityState.Deleted)
                    actionLogs.AddRange(CreateAuditLogs(entryWithState.Entry, entryWithState.State));
            ActionLogs.AddRange(actionLogs);

        }

        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var notAddedEntries = ChangeTracker.Entries().Where(en => en.State != EntityState.Added).ToList();
        var addedEntries = ChangeTracker.Entries().Where(en => en.State == EntityState.Added).ToList();
        if(notAddedEntries.Count > 0)
        {
            var actionLogs = new List<ActionLog>();
            foreach (var entry in notAddedEntries)
                if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
                    actionLogs.AddRange(CreateAuditLogs(entry, entry.State));
            ActionLogs.AddRange(actionLogs);
        }
        if(addedEntries.Count > 0)
        {
            //Extracting the entities and state before saving changes, to get the values for the identity column for the audit logs
            var entriesWithStates = addedEntries.Select(en => new
            {
                Entry = en,
                en.State
            }).ToList();

            await base.SaveChangesAsync(cancellationToken);

            var actionLogs = new List<ActionLog>();
            foreach (var entryWithState in entriesWithStates)
                if (entryWithState.State == EntityState.Added || entryWithState.State == EntityState.Modified || entryWithState.State == EntityState.Deleted)
                    actionLogs.AddRange(CreateAuditLogs(entryWithState.Entry, entryWithState.State));
            ActionLogs.AddRange(actionLogs);

        }
           
        return await base.SaveChangesAsync(cancellationToken);
    }

    private List<ActionLog> CreateAuditLogs(EntityEntry entry,EntityState state)
    {
        var operationType = GetOperationType(state);
        var entityType = entry.Entity.GetType().Name;


        if (entry.State == EntityState.Modified)
        {
            var modifiedProperties = entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name);

            List<ActionLog> resultActionLogs = new();

            foreach (var prop in modifiedProperties)
            {
                var originalValue = entry.Property(prop).OriginalValue;
                var currentValue = entry.Property(prop).CurrentValue;
                resultActionLogs.Add(new ActionLog()
                {
                    Date = DateTime.UtcNow,
                    ItemType = entityType,
                    ItemId = GetEntityId(entry),
                    OperationType = operationType,
                    ColumnName = prop,
                    OldResult = JsonSerializer.Serialize(originalValue),
                    NewResult = JsonSerializer.Serialize(currentValue)
                });
            }
            
            return resultActionLogs;
        }

        var actionLog = new ActionLog
        {
            Date = DateTime.UtcNow,
            ItemType = entityType,
            ItemId = GetEntityId(entry),
            OperationType = operationType,
            ColumnName = null,
            OldResult = null,
            NewResult = null
        };

        return new List<ActionLog> { actionLog };
    }

    private int GetEntityId(EntityEntry entry)
    {
        if (entry.State == EntityState.Added)
            return (int)entry.Property("Id").CurrentValue;
        else if (entry.State == EntityState.Deleted)
            return (int)entry.OriginalValues["Id"];
        else
            return (int)entry.Property("Id").CurrentValue;
    }

    private string GetOperationType(EntityState state)
    {
        switch (state)
        {
            case EntityState.Added:
                return "Created";
            case EntityState.Modified:
                return "Updated";
            case EntityState.Deleted:
                return "Deleted";
            default:
                return "Unknown";
        }
    }

}
