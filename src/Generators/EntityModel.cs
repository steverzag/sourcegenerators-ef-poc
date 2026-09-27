namespace SourceGenerators.EF.Generators;

internal sealed class SchemaModel
{
	public string? Namespace { get; set; }
	public string? BaseType { get; set; }
	public List<EntityModel> Entities { get; } = new();

	public static SchemaModel FromJson(string json)
	{
		if (MiniJson.Parse(json) is not Dictionary<string, object?> root)
			throw new FormatException("Root must be a JSON object");

		var schema = new SchemaModel();
		if (root.TryGetValue("namespace", out var ns) && ns is string s) schema.Namespace = s;
		if (root.TryGetValue("baseType", out var bt) && bt is string b) schema.BaseType = b;

		if (root.TryGetValue("entities", out var ents) && ents is List<object?> list)
		{
			foreach (var item in list)
			{
				if (item is Dictionary<string, object?> e) schema.Entities.Add(EntityModel.From(e));
			}
		}
		return schema;
	}
}

internal sealed class EntityModel
{
	public string Name { get; set; } = "";
	public string? Table { get; set; }
	public string? Extends { get; set; }
	public bool? DbSet { get; set; }
	public bool HasBaseTypeOverride { get; set; }
	public string? BaseType { get; set; }
	public List<PropertyModel> Properties { get; } = new();

	public bool IsExtension => Extends is not null;
	public bool WantsDbSet => DbSet ?? !IsExtension;

	public static EntityModel From(Dictionary<string, object?> e)
	{
		var extends = e.TryGetValue("extends", out var x) ? x as string : null;
		var name = e.TryGetValue("name", out var n) ? n as string : null;
		// For extensions the name is implied by the type being extended.
		name ??= extends is null
			? throw new FormatException("Entity is missing 'name'")
			: extends.Substring(extends.LastIndexOf('.') + 1);

		var m = new EntityModel
		{
			Name = name,
			Extends = extends,
			Table = e.TryGetValue("table", out var t) ? t as string : null,
			DbSet = e.TryGetValue("dbSet", out var ds) && ds is bool b ? b : null,
		};
		if (e.TryGetValue("baseType", out var bt))
		{
			m.HasBaseTypeOverride = true;
			m.BaseType = bt as string;
		}
		if (e.TryGetValue("properties", out var props) && props is List<object?> list)
		{
			foreach (var item in list)
			{
				if (item is Dictionary<string, object?> p) m.Properties.Add(PropertyModel.From(p, m.Name));
			}
		}
		return m;
	}
}

internal sealed class PropertyModel
{
	public string Name { get; set; } = "";
	public string Type { get; set; } = "string";
	public bool Nullable { get; set; }
	public bool Key { get; set; }
	public bool Required { get; set; }
	public int? MaxLength { get; set; }

	public static PropertyModel From(Dictionary<string, object?> p, string entityName)
	{
		return new PropertyModel
		{
			Name = p.TryGetValue("name", out var n) && n is string s ? s : throw new FormatException($"A property on '{entityName}' is missing 'name'"),
			Type = p.TryGetValue("type", out var t) && t is string ts ? ts : "string",
			Nullable = p.TryGetValue("nullable", out var nu) && nu is true,
			Key = p.TryGetValue("key", out var k) && k is true,
			Required = p.TryGetValue("required", out var r) && r is true,
			MaxLength = p.TryGetValue("maxLength", out var ml) && ml is double d ? (int)d : null,
		};
	}
}
