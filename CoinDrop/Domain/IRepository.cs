namespace Domain;



using System.Linq.Expressions;

public interface IRepository<TEntity> where TEntity : class
{
    IQueryable<TEntity> Query();                         // frei kombinierbar (Where/OrderBy/Includes)
    Task<TEntity?> GetAsync(int id, CancellationToken ct = default);
    Task<List<TEntity>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(TEntity entity, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);
    void Update(TEntity entity);
    void Remove(TEntity entity);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}