using CoinDrop;
using Microsoft.EntityFrameworkCore;

namespace Domain;

public class SystemSettingRepository : ARepository<SystemSetting>
{
    private readonly IDbContextFactory<CoinDropContext> _contextFactory;

    public SystemSettingRepository(IDbContextFactory<CoinDropContext> contextFactory) 
        : base(contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<SystemSetting?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.Set<SystemSetting>()
            .FirstOrDefaultAsync(s => s.SettingKey == key && s.IsActive, ct);
    }

    public async Task<List<SystemSetting>> GetByCategoryAsync(string category, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.Set<SystemSetting>()
            .Where(s => s.Category == category && s.IsActive)
            .ToListAsync(ct);
    }

    public async Task<SystemSetting> AddOrUpdateAsync(SystemSetting setting, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        
        var existing = await context.Set<SystemSetting>()
            .FirstOrDefaultAsync(s => s.SettingKey == setting.SettingKey && s.IsActive, ct);
        
        if (existing != null)
        {
            existing.SettingValue = setting.SettingValue;
            existing.DataType = setting.DataType;
            existing.Description = setting.Description;
            existing.IsActive = setting.IsActive;
            existing.LastModified = DateTime.UtcNow;
            existing.ModifiedBy = setting.ModifiedBy;
            
            context.Set<SystemSetting>().Update(existing);
            await context.SaveChangesAsync(ct);
            return existing;
        }
        else
        {
            setting.LastModified = DateTime.UtcNow;
            context.Set<SystemSetting>().Add(setting);
            await context.SaveChangesAsync(ct);
            return setting;
        }
    }

    public async Task<bool> BulkUpdateAsync(List<SystemSetting> settings, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        
        foreach (var setting in settings)
        {
            setting.LastModified = DateTime.UtcNow;
        }
        
        context.Set<SystemSetting>().UpdateRange(settings);
        await context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SoftDeleteAsync(string key, int modifiedBy, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        
        var setting = await context.Set<SystemSetting>()
            .FirstOrDefaultAsync(s => s.SettingKey == key, ct);
            
        if (setting != null)
        {
            setting.IsActive = false;
            setting.LastModified = DateTime.UtcNow;
            setting.ModifiedBy = modifiedBy;
            
            context.Set<SystemSetting>().Update(setting);
            await context.SaveChangesAsync(ct);
            return true;
        }
        return false;
    }

    public async Task<List<SystemSetting>> GetAllActiveAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.Set<SystemSetting>()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Category)
            .ThenBy(s => s.SettingKey)
            .ToListAsync(ct);
    }
}