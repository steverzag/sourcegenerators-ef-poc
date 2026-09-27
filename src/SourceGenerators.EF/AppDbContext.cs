using Microsoft.EntityFrameworkCore;
using SourceGenerators.EF.Entities;
using SourceGenerators.EF.Entities.Users;
using SourceGenerators.EF.Generators;

namespace SourceGenerators.EF
{
	[GenerateEntities("entities.json")]
	internal partial class AppDbContext : DbContext
	{
		public DbSet<User> Users => Set<User>();
	}
}
