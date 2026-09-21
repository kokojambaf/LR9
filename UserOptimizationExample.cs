using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace UserOptimizationExample
{
    // Модель User
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public bool IsActive { get; set; }

        public List<Order> Orders { get; set; } = new List<Order>();
    }

    // Модель Order
    public class Order
    {
        public int Id { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
    }

    // 5.5 DTO для проекции: только имя пользователя и количество заказов
    public record UserOrdersInfo(string Name, int OrdersCount);

    // Контекст базы данных
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Order> Orders { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Ограничения, при нарушении которых БД отклонит вставку (для проверки п.5.7)
            modelBuilder.Entity<User>().Property(u => u.Name).IsRequired();
            modelBuilder.Entity<User>().Property(u => u.Email).IsRequired();
            modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
        }
    }

    // Сервис для работы с пользователями
    public class UserService
    {
        private const string ActiveUsersCacheKey = "ActiveUsers";

        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;

        // 5.8 Продолжительность хранения данных в кэше
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        public UserService(AppDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // 5.4 Получение активных пользователей (только чтение – без отслеживания сущностей)
        public Task<List<User>> GetActiveUsersAsync() =>
            _context.Users
                .AsNoTracking()
                .Where(u => u.IsActive)
                .ToListAsync();

        // 5.5 Получение имён пользователей и количества их заказов через проекцию
        // (вместо Include – загружаются только нужные столбцы, Count вычисляется в SQL)
        public Task<List<UserOrdersInfo>> GetUsersWithOrdersAsync() =>
            _context.Users
                .AsNoTracking()
                .Select(u => new UserOrdersInfo(u.Name, u.Orders.Count))
                .ToListAsync();

        // 5.6, 5.7 Массовое добавление пользователей: AddRange + один SaveChangesAsync в транзакции
        public async Task AddUsersAsync(List<User> users)
        {
            using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    _context.Users.AddRange(users);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (DbUpdateException ex)
                {
                    await transaction.RollbackAsync();
                    // Отменяем добавление, чтобы контекст не пытался сохранить эти сущности повторно
                    _context.ChangeTracker.Clear();
                    Console.WriteLine($"Ошибка добавления пользователей, транзакция отменена: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
        }

        // 5.8 Получение активных пользователей с кэшированием
        public async Task<List<User>> GetCachedActiveUsersAsync()
        {
            if (!_cache.TryGetValue(ActiveUsersCacheKey, out List<User> users))
            {
                Console.WriteLine("  [кэш пуст – запрос к БД]");
                users = await GetActiveUsersAsync();
                _cache.Set(ActiveUsersCacheKey, users, _cacheDuration);
            }
            else
                Console.WriteLine("  [данные взяты из кэша]");

            return users;
        }
    }

    // Класс Program для запуска приложения
    class Program
    {
        static async Task Main(string[] args)
        {
            // ---------- 5.1 – 5.3: файлы и вычисления ----------
            var project = new CodeOptimization();
            string filePath = "data.txt";

            Console.WriteLine("=== 5.2 Запись в файл ===");
            await project.WriteToFileAsync(filePath, "Some data to be written to the file");
            await project.WriteToFileAsync(filePath, $"Строка записана {DateTime.Now:HH:mm:ss}");

            Console.WriteLine("=== 5.1 Чтение из файла ===");
            await project.ReadFromFileAsync(filePath);
            await project.ReadFromFileAsync("missing.txt");

            Console.WriteLine("=== 5.3 Fibonacci Sequence ===");
            for (int i = 0; i < 20; i++)
                Console.WriteLine($"Fib({i}) = {project.Fibonacci(i)}");
            Console.WriteLine($"Fib(90) = {project.Fibonacci(90)} (мгновенно благодаря кэшу)");

            // ---------- 5.4 – 5.8: база данных ----------
            // SQLite в памяти: в отличие от провайдера InMemory поддерживает реальные транзакции и ограничения
            using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
            services.AddMemoryCache();
            services.AddScoped<UserService>();

            var serviceProvider = services.BuildServiceProvider();

            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            await context.Database.EnsureCreatedAsync();

            Console.WriteLine("\n=== 5.6/5.7 Добавление корректных данных ===");
            await userService.AddUsersAsync(new List<User>
            {
                new User { Name = "Alice", Email = "alice@example.com", IsActive = true,
                    Orders = { new Order { ProductName = "Laptop", Quantity = 1 }, new Order { ProductName = "Mouse", Quantity = 2 } } },
                new User { Name = "Bob", Email = "bob@example.com", IsActive = false },
                new User { Name = "Charlie", Email = "charlie@example.com", IsActive = true,
                    Orders = { new Order { ProductName = "Monitor", Quantity = 1 } } }
            });
            await PrintUsersCountAsync(context);

            Console.WriteLine("\n=== 5.7 Добавление некорректных данных (Name = null) ===");
            await userService.AddUsersAsync(new List<User>
            {
                new User { Name = null, Email = "bad@example.com", IsActive = true }
            });
            await PrintUsersCountAsync(context);

            Console.WriteLine("\n=== 5.7 Вначале корректные, затем некорректные данные ===");
            await userService.AddUsersAsync(new List<User>
            {
                new User { Name = "Diana", Email = "diana@example.com", IsActive = true },
                new User { Name = "Eve", Email = "eve@example.com", IsActive = true },
                new User { Name = "Duplicate", Email = "alice@example.com", IsActive = true } // повтор Email
            });
            await PrintUsersCountAsync(context);
            Console.WriteLine("  (Diana и Eve не сохранены – транзакция откатилась целиком)");

            Console.WriteLine("\n=== 5.4 Active Users (AsNoTracking) ===");
            foreach (var user in await userService.GetActiveUsersAsync())
                Console.WriteLine($"{user.Name} - {user.Email}");
            Console.WriteLine($"Отслеживаемых сущностей в контексте: {context.ChangeTracker.Entries().Count()}");

            Console.WriteLine("\n=== 5.5 Users with Orders (проекция Select) ===");
            foreach (var info in await userService.GetUsersWithOrdersAsync())
                Console.WriteLine($"{info.Name} - Orders: {info.OrdersCount}");

            Console.WriteLine("\n=== 5.8 Cached Active Users ===");
            for (int i = 1; i <= 3; i++)
            {
                Console.WriteLine($"Вызов {i}:");
                var cached = await userService.GetCachedActiveUsersAsync();
                Console.WriteLine($"  Получено пользователей: {cached.Count} ({string.Join(", ", cached.Select(u => u.Name))})");
            }
        }

        private static async Task PrintUsersCountAsync(AppDbContext context) =>
            Console.WriteLine($"Пользователей в БД: {await context.Users.CountAsync()}");
    }
}
