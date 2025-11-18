using System.Linq.Expressions;
using CoinDrop;
using Microsoft.EntityFrameworkCore;

namespace Domain;
public abstract class ARepository<TEntity> : IRepository<TEntity> where TEntity : class
{
    protected readonly CoinDropContext _context;
    protected readonly DbSet<TEntity> _entitySet;

    protected ARepository(CoinDropContext context)
    {
        _context = context;
        _entitySet = _context.Set<TEntity>();
    }

    // Wie im 1. Repo: AddAsync mit SaveChangesAsync
    public virtual async Task AddAsync(TEntity entity, CancellationToken ct = default)
    {
        await _entitySet.AddAsync(entity, ct);
        await _context.SaveChangesAsync(ct);
    }

    // Wie im 1. Repo: AddRange (ohne Async-Suffix), aber trotzdem async/await + SaveChangesAsync
    public virtual async Task AddRange(IEnumerable<TEntity> entities, CancellationToken ct = default)
    {
        await _entitySet.AddRangeAsync(entities, ct);
        await _context.SaveChangesAsync(ct);
    }

    // Wie im 1. Repo: Query via Expression<Func<TEntity, bool>>
    public virtual async Task<TEntity?> GetByIdAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken ct = default)
    {
        return await _entitySet
            .Where(predicate)
            .FirstOrDefaultAsync(ct);
    }

    // Wie im 1. Repo: Paging (skip, take)
    public virtual async Task<IEnumerable<TEntity>> GetAllAsync(
        int skip,
        int take,
        CancellationToken ct = default)
    {
        return await _entitySet
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    // Wie im 1. Repo: UpdateAsync mit SaveChangesAsync
    public virtual async Task UpdateAsync(TEntity entity, CancellationToken ct = default)
    {
        _entitySet.Update(entity);
        await _context.SaveChangesAsync(ct);
    }

    // Wie im 1. Repo: UpdateRange + SaveChangesAsync
    public virtual async Task UpdateRange(IEnumerable<TEntity> entities, CancellationToken ct = default)
    {
        _entitySet.UpdateRange(entities);
        await _context.SaveChangesAsync(ct);
    }

    // Wie im 1. Repo: DeleteRangeAsync
    public virtual async Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default)
    {
        _entitySet.RemoveRange(entities);
        await _context.SaveChangesAsync(ct);
    }

    // Wie im 1. Repo: DeleteAsync
    public virtual async Task DeleteAsync(TEntity entity, CancellationToken ct = default)
    {
        _entitySet.Remove(entity);
        await _context.SaveChangesAsync(ct);
    }

    // ---- Zusätzliche Funktionalität aus dem 2. Repo beibehalten ----

    // Entspricht der alten GetAsync(int id, ...)
    public virtual Task<TEntity?> GetAsync(int id, CancellationToken ct = default)
        => _entitySet.FindAsync(new object?[] { id }, ct).AsTask();

    // Entspricht der alten GetAllAsync() ohne Paging
    public virtual Task<List<TEntity>> GetAllAsync(CancellationToken ct = default)
        => _entitySet.ToListAsync(ct);

    // Query wie vorher
    public virtual IQueryable<TEntity> Query() => _entitySet.AsQueryable();

    // Explizites SaveChangesAsync wie vorher verfügbar
    public virtual Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);
}