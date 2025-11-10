using CoinDrop;

namespace Domain;

using Microsoft.EntityFrameworkCore;

public abstract class ARepository<TEntity> : IRepository<TEntity> where TEntity : class
{
    protected readonly CoinDropContext Ctx;
    protected readonly DbSet<TEntity> Set;

    protected ARepository(CoinDropContext ctx)
    {
        Ctx = ctx;
        Set = Ctx.Set<TEntity>();
    }

    public virtual IQueryable<TEntity> Query() => Set.AsQueryable();

    public virtual Task<TEntity?> GetAsync(int id, CancellationToken ct = default)
        => Set.FindAsync(new object?[] { id }, ct).AsTask();

    public virtual Task<List<TEntity>> GetAllAsync(CancellationToken ct = default)
        => Set.ToListAsync(ct);

    public virtual Task AddAsync(TEntity entity, CancellationToken ct = default)
        => Set.AddAsync(entity, ct).AsTask();

    public virtual Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default)
        => Set.AddRangeAsync(entities, ct);

    public virtual void Update(TEntity entity) => Set.Update(entity);

    public virtual void Remove(TEntity entity) => Set.Remove(entity);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => Ctx.SaveChangesAsync(ct);
}