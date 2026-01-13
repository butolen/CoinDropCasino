using System.Linq.Expressions;
using Domain;
using Microsoft.EntityFrameworkCore;

public abstract class ARepository<TEntity> : IRepository<TEntity> where TEntity : class
{
    private readonly IDbContextFactory<CoinDropContext> _contextFactory;

    protected ARepository(IDbContextFactory<CoinDropContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    // Helper method to get context
    private async Task<CoinDropContext> GetContextAsync(CancellationToken ct = default)
    {
        return await _contextFactory.CreateDbContextAsync(ct);
    }

    // Wie im 1. Repo: AddAsync mit SaveChangesAsync
    public virtual async Task AddAsync(TEntity entity, CancellationToken ct = default)
    {
        await using var context = await GetContextAsync(ct);
        context.Set<TEntity>().Add(entity);
        await context.SaveChangesAsync(ct);
    }

    // Wie im 1. Repo: AddRange (ohne Async-Suffix), aber trotzdem async/await + SaveChangesAsync
    public virtual async Task AddRange(IEnumerable<TEntity> entities, CancellationToken ct = default)
    {
        await using var context = await GetContextAsync(ct);
        context.Set<TEntity>().AddRange(entities);
        await context.SaveChangesAsync(ct);
    }

    // Wie im 1. Repo: Query via Expression<Func<TEntity, bool>>
    public virtual async Task<TEntity?> GetByIdAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken ct = default)
    {
        await using var context = await GetContextAsync(ct);
        return await context.Set<TEntity>()
            .Where(predicate)
            .FirstOrDefaultAsync(ct);
    }

    // Wie im 1. Repo: Paging (skip, take)
    public virtual async Task<IEnumerable<TEntity>> GetAllAsync(
        int skip,
        int take,
        CancellationToken ct = default)
    {
        await using var context = await GetContextAsync(ct);
        return await context.Set<TEntity>()
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    // Wie im 1. Repo: UpdateAsync mit SaveChangesAsync
    public virtual async Task UpdateAsync(TEntity entity, CancellationToken ct = default)
    {
        await using var context = await GetContextAsync(ct);
        context.Set<TEntity>().Update(entity);
        await context.SaveChangesAsync(ct);
    }

    // Wie im 1. Repo: UpdateRange + SaveChangesAsync
    public virtual async Task UpdateRange(IEnumerable<TEntity> entities, CancellationToken ct = default)
    {
        await using var context = await GetContextAsync(ct);
        context.Set<TEntity>().UpdateRange(entities);
        await context.SaveChangesAsync(ct);
    }

    // Wie im 1. Repo: DeleteRangeAsync
    public virtual async Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default)
    {
        await using var context = await GetContextAsync(ct);
        context.Set<TEntity>().RemoveRange(entities);
        await context.SaveChangesAsync(ct);
    }

    // Wie im 1. Repo: DeleteAsync
    public virtual async Task DeleteAsync(TEntity entity, CancellationToken ct = default)
    {
        await using var context = await GetContextAsync(ct);
        context.Set<TEntity>().Remove(entity);
        await context.SaveChangesAsync(ct);
    }

    // ---- Zusätzliche Funktionalität aus dem 2. Repo beibehalten ----

    // Entspricht der alten GetAsync(int id, ...)
    public virtual async Task<TEntity?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var context = await GetContextAsync(ct);
        return await context.Set<TEntity>().FindAsync(new object?[] { id }, ct);
    }

    // Entspricht der alten GetAllAsync() ohne Paging
    public virtual async Task<List<TEntity>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await GetContextAsync(ct);
        return await context.Set<TEntity>().ToListAsync(ct);
    }

    // Query wie vorher - ABER: Diese Methode ist problematisch mit Factory!
    // Der Context wird disposed bevor die Query ausgeführt wird
    public virtual IQueryable<TEntity> Query()
    {
        // ACHTUNG: Diese Methode funktioniert nicht gut mit Factory!
        // Besser: Eine async Query-Methode verwenden
        var context = _contextFactory.CreateDbContext();
        return context.Set<TEntity>().AsQueryable();
        
        // Alternative: Statt Query() verwende ExecuteQueryAsync
    }

    // NEUE Methode: Async Query (besser für Factory)
    public virtual async Task<List<TEntity>> ExecuteQueryAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> queryBuilder,
        CancellationToken ct = default)
    {
        await using var context = await GetContextAsync(ct);
        var query = queryBuilder(context.Set<TEntity>());
        return await query.ToListAsync(ct);
    }

    // NEUE Methode: FirstOrDefault mit Query-Builder
    public virtual async Task<TEntity?> QueryFirstOrDefaultAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> queryBuilder,
        CancellationToken ct = default)
    {
        await using var context = await GetContextAsync(ct);
        var query = queryBuilder(context.Set<TEntity>());
        return await query.FirstOrDefaultAsync(ct);
    }

    // Explizites SaveChangesAsync - NICHT MEHR NÖTIG, da jeder Context eigenes SaveChanges hat
    // Diese Methode können wir entfernen oder so implementieren:
    public virtual async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        await using var context = await GetContextAsync(ct);
        return await context.SaveChangesAsync(ct);
    }
}