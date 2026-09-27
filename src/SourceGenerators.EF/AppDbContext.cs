using Microsoft.EntityFrameworkCore;
using SourceGenerators.EF.Entities;
using SourceGenerators.EF.Entities.Users;
using SourceGenerators.EF.Generators;
using System.CodeDom.Compiler;
using System.Reflection;

namespace SourceGenerators.EF
{
	[GenerateEntities("entities.json")]
	internal partial class AppDbContext : DbContext
	{
		public DbSet<User> Users => Set<User>();

		protected override void OnModelCreating(ModelBuilder b)
		{
			base.OnModelCreating(b);

			// Generated configurations first, then hand-written ones, so hand-written settings
			// win wherever both configure the same thing.
			ApplyGeneratedConfiguration(b);
			b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly, t => !t.IsDefined(typeof(GeneratedCodeAttribute)));
		}
	}
}
