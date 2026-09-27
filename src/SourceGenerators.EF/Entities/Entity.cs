using System;
using System.Collections.Generic;
using System.Text;

namespace SourceGenerators.EF.Entities
{
	public class Entity
	{
		public Guid Id { get; init; } = Guid.CreateVersion7();
		public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
	}
}
