using CoinDrop;
using Microsoft.EntityFrameworkCore;

namespace Domain;


public class SystemSettingRepository : ARepository<SystemSetting>
{
    public SystemSettingRepository(CoinDropContext context) : base(context)
    {
    }

    public async Task<SystemSetting?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        return await _entitySet
            .FirstOrDefaultAsync(s => s.SettingKey == key && s.IsActive, ct);
    }

    public async Task<List<SystemSetting>> GetByCategoryAsync(string category, CancellationToken ct = default)
    {
        return await _entitySet
            .Where(s => s.Category == category && s.IsActive)
            .ToListAsync(ct);
    }

    public async Task<SystemSetting> AddOrUpdateAsync(SystemSetting setting, CancellationToken ct = default)
    {
        var existing = await GetByKeyAsync(setting.SettingKey, ct);
        
        if (existing != null)
        {
            existing.SettingValue = setting.SettingValue;
            existing.DataType = setting.DataType;
            existing.Description = setting.Description;
            existing.IsActive = setting.IsActive;
            existing.LastModified = DateTime.UtcNow;
            existing.ModifiedBy = setting.ModifiedBy;
            
            await UpdateAsync(existing, ct);
            return existing;
        }
        else
        {
            setting.LastModified = DateTime.UtcNow;
            await AddAsync(setting, ct);
            return setting;
        }
    }

    public async Task<bool> BulkUpdateAsync(List<SystemSetting> settings, CancellationToken ct = default)
    {
        foreach (var setting in settings)
        {
            setting.LastModified = DateTime.UtcNow;
        }
        
        await UpdateRange(settings, ct);
        return true;
    }

    public async Task<bool> SoftDeleteAsync(string key, int modifiedBy, CancellationToken ct = default)
    {
        var setting = await GetByKeyAsync(key, ct);
        if (setting != null)
        {
            setting.IsActive = false;
            setting.LastModified = DateTime.UtcNow;
            setting.ModifiedBy = modifiedBy;
            await UpdateAsync(setting, ct);
            return true;
        }
        return false;
    }

    public async Task<List<SystemSetting>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await _entitySet
            .Where(s => s.IsActive)
            .OrderBy(s => s.Category)
            .ThenBy(s => s.SettingKey)
            .ToListAsync(ct);
    }
}