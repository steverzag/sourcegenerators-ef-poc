using System;
using System.Collections.Generic;
using System.Text;

namespace SourceGenerators.EF.Entities.Users
{
	internal partial class User : Entity
	{
		public string Email { get; set; } = null!;
		public string Name { get; set; } = null!;
		public string Password { get; set; } = null!;
	}
}
