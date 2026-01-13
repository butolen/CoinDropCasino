namespace Domain;



using System.Linq.Expressions;

public interface IRepository<TEntity> where TEntity : class
{
    // Frei kombinierbare Query (Where/OrderBy/Include usw.)
    IQueryable<TEntity> Query();

    // Primärschlüsselabfrage (wie vorher)
    Task<TEntity?> GetAsync(int id, CancellationToken ct = default);

    // Komplettliste (ohne Skip/Take), wie im zweiten Repo vorhanden
    Task<List<TEntity>> GetAllAsync(CancellationToken ct = default);

    // Expression-basierte Abfrage, wie im ersten Repo
    Task<TEntity?> GetByIdAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);

    // Paging-Version wie im ersten Repo
    Task<IEnumerable<TEntity>> GetAllAsync(int skip, int take, CancellationToken ct = default);

    Task<List<TEntity>> ExecuteQueryAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> queryBuilder,
        CancellationToken ct = default);
    // Add (einzeln)
    Task AddAsync(TEntity entity, CancellationToken ct = default);

    // AddRange
    Task AddRange(IEnumerable<TEntity> entities, CancellationToken ct = default);

    // Update (async, weil SaveChanges in der Implementierung async ist)
    Task UpdateAsync(TEntity entity, CancellationToken ct = default);

    // UpdateRange
    Task UpdateRange(IEnumerable<TEntity> entities, CancellationToken ct = default);

    // Delete (einzeln)
    Task DeleteAsync(TEntity entity, CancellationToken ct = default);

    // DeleteRange
    Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    // Manuelles Commit (falls du mehrere Änderungen stapelst)
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}