using ClinicBooking.Domain;

namespace ClinicBooking.Infrastructure;

public sealed class Repository<T>(ClinicDbContext db) : IRepository<T> where T : class
{
    public IEnumerable<T> GetAll() => db.Set<T>().ToList();
    public T? GetById(int id) => db.Set<T>().Find(id);
    public void Add(T entity) => db.Set<T>().Add(entity);
}
