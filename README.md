# SourceGenerators.EF (POC)

A proof of concept that uses a **Roslyn incremental source generator** to build Entity Framework Core entities from a JSON schema at compile time.

Instead of writing entity classes and `DbSet<T>` properties by hand, you describe them in a JSON file. The generator reads that file during compilation and emits the C# code. The generated code is part of the same compilation, so the types are available right away with IntelliSense and compile-time checks, and nothing is generated at runtime.

## What it does

For each `DbContext` marked with `[GenerateEntities("<schema>.json")]`, the generator:

1. **Creates new entity classes** for each entry in the schema, with properties and an optional shared base class. The classes carry no attributes.
2. **Extends existing hand-written classes** (`"extends"`). It adds properties to a `partial` class you already wrote, so one entity can be part hand-written and part generated.
3. **Generates Fluent API configuration**: one `IEntityTypeConfiguration<T>` per entity (keys, required, max length, table) in a `.Configuration` sub-namespace, plus an `ApplyGeneratedConfiguration(ModelBuilder)` method on the context that applies them. The classes are marked `[GeneratedCode]` so an assembly scan for hand-written configurations can skip them.
4. **Adds `DbSet<T>` properties** to the context for the generated entities.
5. **Reports compile errors** (`EFGEN001`–`EFGEN007`) when the schema is invalid or conflicts with existing code.

## Solution layout

```
SourceGenerators.EF.slnx
src/
├── Generators/                  # The source generator (netstandard2.0, loaded as an analyzer)
│   ├── EntityGenerator.cs       # Incremental pipeline, diagnostics and code emission
│   ├── EntityModel.cs           # Schema / Entity / Property models parsed from JSON
│   ├── MiniJson.cs              # Small built-in JSON parser (no System.Text.Json dependency)
│   ├── EquatableArray.cs        # ImmutableArray with value equality, needed for incremental caching
│   ├── IsExternalInit.cs        # Polyfill so records work on netstandard2.0
│   └── Properties/launchSettings.json  # "Debug Generator" profile
└── SourceGenerators.EF/         # Sample console app (net10.0) that uses the generator
    ├── AppDbContext.cs          # [GenerateEntities("entities.json")] partial DbContext
    ├── entities.json            # The schema
    ├── Entities/Entity.cs       # Base class (Id, CreatedAt) used by the generated entities
    ├── Entities/Users/User.cs   # Hand-written partial class that the schema extends
    └── Program.cs               # Uses the generated types
```

## How to use it

### 1. Reference the generator as an analyzer

```xml
<ItemGroup>
  <!-- Pass the schema to the compiler so the generator can read it -->
  <AdditionalFiles Include="entities.json" />
</ItemGroup>

<ItemGroup>
  <!-- Roslyn runs the generator; its DLL is not referenced at runtime -->
  <ProjectReference Include="..\Generators\Generators.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

The schema **must** be an `<AdditionalFiles>` item. Otherwise the generator cannot see it and reports `EFGEN003`.

`"table"` is emitted as `ToTable(...)`, which lives in `Microsoft.EntityFrameworkCore.Relational`. Any relational provider package (SqlServer, Npgsql, Sqlite, ...) brings it in. The sample references it directly because it has no provider. Without it, the generator reports `EFGEN007`.

### 2. Mark a partial DbContext and apply the configuration

```csharp
using SourceGenerators.EF.Generators;
using System.CodeDom.Compiler;
using System.Reflection;

[GenerateEntities("entities.json")]
internal partial class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>(); // hand-written DbSets can coexist with generated ones

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Generated configurations first, then hand-written ones, so hand-written settings
        // win wherever both configure the same thing.
        ApplyGeneratedConfiguration(b);
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly, t => !t.IsDefined(typeof(GeneratedCodeAttribute)));
    }
}
```

Unlike data annotations, fluent configuration is only used when something applies it, so **`OnModelCreating` must apply the generated configurations**. Otherwise the entities are still mapped, but without the schema's keys, lengths or table names. The sample does it in two steps:

1. **`ApplyGeneratedConfiguration(b)`** is a `private static` method the generator adds to the context. It applies exactly this context's generated configurations, without reflection. It is always generated (empty if there is nothing to configure), so calling it compiles even while the schema has errors.
2. **`ApplyConfigurationsFromAssembly`** picks up any `IEntityTypeConfiguration<T>` you write by hand, so new ones are applied without touching `OnModelCreating`. Generated configuration classes are marked `[GeneratedCode]`, and the filter skips them, so they aren't applied a second time.

A fluent setting overrides the same setting applied earlier, so this order means **hand-written configuration wins** whenever both set the same thing (for example `HasMaxLength` on the same property). Settings that don't overlap simply combine. The generator only sets `ToTable`, `HasKey`, `IsRequired` and `HasMaxLength`, and only on properties declared in the schema.

Two caveats with the filter:

- It skips every type marked `[GeneratedCode]`, not just this generator's. To be stricter, compare `GeneratedCodeAttribute.Tool` with `"SourceGenerators.EF.Generators.EntityGenerator"`.
- If more than one context lives in the same assembly, each context's scan also picks up the other's hand-written configurations. Only the generated half is scoped per context.

You don't need to reference any package for `GenerateEntitiesAttribute`. The generator emits it into your project (`RegisterPostInitializationOutput`).

The path is resolved relative to the file that declares the context. If no file matches there, the generator uses any additional file whose path ends with the given path (for example `"Schemas/app.json"`).

### 3. Describe the entities

```json
{
  "namespace": "SourceGenerators.EF.Entities",
  "baseType": "SourceGenerators.EF.Entities.Entity",
  "entities": [
    {
      "name": "Customer",
      "table": "Customers",
      "properties": [
        { "name": "Name",  "type": "string", "required": true, "maxLength": 100 },
        { "name": "Email", "type": "string", "nullable": true, "maxLength": 200 }
      ]
    },
    {
      "extends": "SourceGenerators.EF.Entities.Users.User",
      "properties": [
        { "name": "PhoneNumber", "type": "string", "nullable": true, "maxLength": 32 },
        { "name": "IsActive",    "type": "bool" }
      ]
    }
  ]
}
```

## Schema reference

### Root

| Key         | Type     | Description |
|-------------|----------|-------------|
| `namespace` | string   | Namespace for new entities. Defaults to `<ContextNamespace>.Entities`. |
| `baseType`  | string   | Fully-qualified base class for all new entities. Optional. |
| `entities`  | array    | Entity definitions (see below). |

### Entity

| Key          | Type          | Description |
|--------------|---------------|-------------|
| `name`       | string        | Class name. Required for new entities; for extensions it defaults to the extended type's name. |
| `extends`    | string        | Fully-qualified name of an existing **partial** class to enrich instead of declaring a new one. |
| `table`      | string        | Configures `ToTable("...")` (requires Relational, see `EFGEN007`) and is also used as the `DbSet` property name. |
| `baseType`   | string / null | Overrides the root `baseType` for this entity. Use `null` to opt out of inheritance. |
| `dbSet`      | bool          | Whether to generate a `DbSet`. Default: `true` for new entities, `false` for extensions. |
| `properties` | array         | Property definitions (see below). |

If `table` is not set, the `DbSet` is named `<Name>s` (for example `Customer` → `Customers`).

### Property

| Key         | Type   | Default    | Description |
|-------------|--------|------------|-------------|
| `name`      | string | (required) | Property name. |
| `type`      | string | `string`   | C# type, emitted as written (`Guid`, `decimal`, `DateTimeOffset`, ...). |
| `nullable`  | bool   | `false`    | Appends `?` to the type. |
| `key`       | bool   | `false`    | Configures `HasKey(...)`. Several `key` properties form a composite key. |
| `required`  | bool   | `false`    | Configures `IsRequired()`. |
| `maxLength` | number | —          | Configures `HasMaxLength(n)`. |

Non-nullable reference types (`string`, `byte[]`, `object`) get `= null!` so the compiler doesn't warn about uninitialized members.

A configuration class is only generated for entities that have something to configure (`table`, `key`, `required` or `maxLength`), because applying one also adds the entity to the model.

## What gets generated

For the sample schema, the generator emits these files (visible under `obj/Debug/net10.0/generated/...` because the sample sets `EmitCompilerGeneratedFiles=true`):

| File | Content |
|------|---------|
| `GenerateEntitiesAttribute.g.cs` | The marker attribute. |
| `AppDbContext.Customer.g.cs` | New `Customer` entity. |
| `AppDbContext.Order.g.cs` | New `Order` entity. |
| `AppDbContext.User.g.cs` | Extra properties for the hand-written `User`. |
| `AppDbContext.CustomerConfiguration.g.cs` | `CustomerConfiguration` (same for `Order` and `User`). |
| `AppDbContext.Configuration.g.cs` | `ApplyGeneratedConfiguration` on the context. |
| `AppDbContext.DbSets.g.cs` | `Customers` and `Orders` DbSets. |

**New entity** (`AppDbContext.Customer.g.cs`):

```csharp
namespace SourceGenerators.EF.Entities;

public partial class Customer : global::SourceGenerators.EF.Entities.Entity
{
    public string Name { get; set; } = null!;
    public string? Email { get; set; }
}
```

**Configuration** (`AppDbContext.CustomerConfiguration.g.cs`). It goes in the entity's namespace plus `.Configuration`. For an extension that's the extended type's namespace, so `User` gets `SourceGenerators.EF.Entities.Users.Configuration`. Classes are `internal` because an extended entity may itself be internal:

```csharp
using Microsoft.EntityFrameworkCore;

namespace SourceGenerators.EF.Entities.Configuration;

[GeneratedCode("SourceGenerators.EF.Generators.EntityGenerator", "1.0.0.0")]
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Email).HasMaxLength(200);
    }
}
```

**Apply method** (`AppDbContext.Configuration.g.cs`):

```csharp
partial class AppDbContext
{
    private static void ApplyGeneratedConfiguration(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new global::SourceGenerators.EF.Entities.Configuration.CustomerConfiguration());
        modelBuilder.ApplyConfiguration(new global::SourceGenerators.EF.Entities.Configuration.OrderConfiguration());
        modelBuilder.ApplyConfiguration(new global::SourceGenerators.EF.Entities.Users.Configuration.UserConfiguration());
    }
}
```

(The real output uses `global::`-qualified names throughout; they're shortened here for readability.)

**Extension** (`AppDbContext.User.g.cs`). The generated half leaves out accessibility and the base type, so the hand-written half is the only place they are declared:

```csharp
namespace SourceGenerators.EF.Entities.Users;

partial class User
{
    public string? PhoneNumber { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public bool IsActive { get; set; }
}
```

**DbSets** (`AppDbContext.DbSets.g.cs`):

```csharp
namespace SourceGenerators.EF;

partial class AppDbContext
{
    public DbSet<global::SourceGenerators.EF.Entities.Customer> Customers => Set<global::SourceGenerators.EF.Entities.Customer>();
    public DbSet<global::SourceGenerators.EF.Entities.Order> Orders => Set<global::SourceGenerators.EF.Entities.Order>();
}
```

## How the generator works internally

`EntityGenerator` implements `IIncrementalGenerator`. Its pipeline has these steps:

```
[GenerateEntities] classes ─┐
  (ForAttributeWithMetadataName)
                            ├─► ResolveSchema ─┐
*.json AdditionalFiles ─────┘                  ├─► CollectTypeFacts ─► Emit
                                  Compilation ─┘
```

1. **Find contexts.** `ForAttributeWithMetadataName` picks up every class with the attribute and turns it into a `ContextTarget` record (namespace, name, whether it is `partial`, schema path, attribute location).
2. **Find the schema.** Each context is combined with the collected `.json` additional files, and the file that matches the attribute path is selected.
3. **Look up referenced types.** For each `baseType` and `extends` in the schema, the generator looks up the symbol in the `Compilation`. It records whether the type exists, whether it is `partial`, and the names of all its instance properties and fields, including inherited ones. The result is stored in an equatable `TypeFacts` record. It also records whether `Microsoft.EntityFrameworkCore.Relational` is referenced, which `ToTable` needs.
4. **Emit.** The generator validates everything, reports diagnostics and adds one source file per entity and per configuration, plus one for `ApplyGeneratedConfiguration` and one for the DbSets.

### Why everything is a record / `EquatableArray`

Incremental generators cache each step's output and skip later steps when the output is equal to the previous run. Step 3 reruns on every keystroke because it depends on the `Compilation`. It returns plain, value-equatable data (not symbols or syntax nodes), so step 4 only runs again when the schema, the context or a referenced type actually changes. `EquatableArray<T>` exists because `ImmutableArray<T>` compares by reference. For the same reason, `LocationInfo` is used instead of `Location`, which holds a reference to the syntax tree.

### Why a custom JSON parser

Generators run inside the compiler and must target `netstandard2.0`. Adding `System.Text.Json` as a dependency of an analyzer causes assembly-loading problems, so `MiniJson` is a small self-contained parser that returns dictionaries, lists, strings, doubles, bools and nulls.

## Diagnostics

| ID        | When |
|-----------|------|
| `EFGEN001` | The schema is not valid JSON or is missing required fields (for example an entity without `name`). |
| `EFGEN002` | A schema property already exists on the base type or on the extended type. The error points at the property inside the JSON file. |
| `EFGEN003` | The schema file was not found among the project's `AdditionalFiles`. |
| `EFGEN004` | The `DbContext` is not declared `partial`. |
| `EFGEN005` | The type named in `extends` does not exist. Use the fully-qualified name. |
| `EFGEN006` | The type named in `extends` is not declared `partial`. |
| `EFGEN007` | An entity sets `table` but `Microsoft.EntityFrameworkCore.Relational` is not referenced, so `ToTable` is unavailable. |

A `baseType` that cannot be resolved is left to the compiler, which reports `CS0246` on the generated file.

## Building and running

Requires the .NET 10 SDK.

```bash
dotnet build
dotnet run --project src/SourceGenerators.EF
```

Expected output:

```
Ada (no email) created at 2026-09-27T00:46:55.7443131+00:00
Order 01a0e054-... for customer 01a0e054-...: 42.5
User Grace phone=+1 555 0100 active=True lastLogin=never
Users, Customers, Orders
```

`Id` and `CreatedAt` come from the `Entity` base class, `PhoneNumber`/`IsActive`/`LastLoginAt` are added to `User` by the schema, and `Customers`/`Orders` are generated DbSets next to the hand-written `Users`.

### Debugging the generator

In Visual Studio, set `Generators` as the startup project and run the **Debug Generator** profile (`IsRoslynComponent` is enabled). This attaches the debugger to the compiler while it builds `SourceGenerators.EF`, so you can step through `EntityGenerator`.

After editing the generator, you may need to restart the IDE (or run `dotnet build-server shutdown`), because the compiler server caches the loaded analyzer.

## Limitations (it's a POC)

- Only scalar properties are supported. There are no navigation properties, relationships or indexes yet.
- Nothing checks that `OnModelCreating` actually applies the generated configurations. If it doesn't, they are silently skipped.
- The schema can only configure properties it declares. Fluent API could also configure hand-written or inherited properties (for example `maxLength` on `User.Email`), but `EFGEN002` still rejects those.
- Property types are emitted exactly as written, so they must be resolvable from the generated file (types in `System` work through implicit usings).
- `EFGEN002` finds the location in the JSON with a text search, so the error position is approximate.
