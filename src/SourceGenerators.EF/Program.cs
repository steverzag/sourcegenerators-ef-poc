using SourceGenerators.EF;
using SourceGenerators.EF.Entities;
using SourceGenerators.EF.Entities.Users;

// Id and CreatedAt come from the Entity base class, not from entities.json
var customer = new Customer { Name = "Ada" };
var order = new Order { CustomerId = customer.Id, Total = 42.5m };

Console.WriteLine($"{customer.Name} ({customer.Email ?? "no email"}) created at {customer.CreatedAt:O}");
Console.WriteLine($"Order {order.Id} for customer {order.CustomerId}: {order.Total}");

// User is hand-written; PhoneNumber / LastLoginAt / IsActive come from the schema
var user = new User { Name = "Grace", Email = "grace@example.com", Password = "hunter2", PhoneNumber = "+1 555 0100", IsActive = true };
Console.WriteLine($"User {user.Name} phone={user.PhoneNumber} active={user.IsActive} lastLogin={user.LastLoginAt?.ToString() ?? "never"}");

// DbSets: Users is hand-written, Customers/Orders are generated
Console.WriteLine(string.Join(", ",
    typeof(AppDbContext).GetProperties().Where(p => p.PropertyType.Name.StartsWith("DbSet")).Select(p => p.Name)));
