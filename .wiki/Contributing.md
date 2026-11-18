# Contributing

These conventions apply to new and modified Blueprint code, tests, and documentation examples. Apply them to the parts you change; keep unrelated code and formatting intact.

## Language

- Use English for class, variable, method, function, file, constant, and other identifier names.
- Write **all comments in English**, including XML documentation, inline comments, and block comments in library source, tests, and project files.
- Write the user manual and its examples in English.
- Preserve existing application text, stored data, translation examples, and external identifiers unless a change is explicitly requested. The comment-language rule does not require translating string literals.

## Explicit types: no var

Always declare C# variables with explicit types. This also applies to loop variables and documentation examples. **Do not use `var`.**

Use:

```csharp
int productCount = 0;
string productName = "Camera";
List<string> productNames = new List<string>();
```

Do not use:

```csharp
var productCount = 0;
var productName = "Camera";
var productNames = new List<string>();
```

## Braces and statement formatting

Use **Allman / BSD brace style**. Put the opening brace on the next line after the declaration or statement. Put the closing brace on its own line, aligned with the start of that declaration or statement.

This applies to classes, interfaces, structs, enums, constructors, methods, functions, conditionals, loops, switch statements, try/catch/finally blocks, and anonymous blocks.

Keep method calls, constructor calls, assignments, return statements, and conditions on one line when practical. Do not put each argument on a separate line solely for formatting.

Line breaks are appropriate when a line becomes difficult to read, complex logic needs them, or the surrounding code already uses a multiline fluent API or builder style. Do not reformat unrelated sections.

## Resource disposal: using blocks

**Always use `using (...)` with an explicit block for disposable resources. Do not use `using` declarations without a block.** This rule applies to new and modified C# code in the library, applications, tests, demos, and documentation examples.

Put the opening and closing braces on separate lines, following Allman style, and declare the resource with an explicit type. Keep all operations that need the resource inside its block.

For example, with the connection settings, languages, and Product contract already defined:

```csharp
using (BlueprintDatabase db = new SqlDatabase("MyEshopDatabase", sqlServerConnectionString, languages))
{
    db.RegisterEntity<Product>();
}
```

Do not write `using BlueprintDatabase db = new SqlDatabase(...);`. For asynchronous disposal, use `await using (...)` with an explicit block as well, instead of an `await using` declaration.

This rule concerns resource disposal. Namespace imports such as `using Blueprint.Database;` remain unchanged. When converting existing code, preserve resource lifetime and disposal order without reformatting unrelated code.

## Naming

| Element | Rule | Examples |
| --- | --- | --- |
| Classes | PascalCase, without technical class prefixes or suffixes | `UserManager`; avoid `CUserManager` and `UserManagerClass` |
| Enums | `E` followed by PascalCase | `EType` |
| Class fields | `f` followed by PascalCase, including static fields and fields of any visibility | `fName`, `fPostalCode`, `fUserCount` |
| Constants | UPPER_SNAKE_CASE | `MAX_RETRY_COUNT` |
| Ordinary custom methods and functions | PascalCase | `LoadUsers`, `DeleteUser` |
| Getter and setter methods | camelCase | `getName`, `setName` |
| Local variables and parameters | camelCase, without the field prefix | `productCount`, `userID`, `isEnabled` |

Use descriptive names. The `f` prefix is reserved for class fields; never apply it to local variables, parameters, or temporary values. Constants follow their own UPPER_SNAKE_CASE rule.

## Volatile fields and thread synchronization

**Do not add `volatile`, `Volatile.Read`, or `Volatile.Write` merely because multiple threads read a field. First establish when it can change and how its initialization and lifetime are synchronized.**

Blueprint uses one database instance per application. Initialization must complete before consumer threads start, and disposal must run only after all consumers, including queued operations and cache callbacks, have stopped. Thread startup and shutdown must provide the synchronization that publishes initialization and waits for consumers to finish. Under this lifecycle, `fActiveInstance` is read directly and is not declared `volatile`:

```csharp
private static BlueprintDatabase? fActiveInstance;
```

Keep `Interlocked` operations where atomic updates are required, such as publishing the active database only when the field is null or clearing it only when it still refers to the disposing instance. Do not replace compare-and-exchange with a separate check and assignment.

If another field can change while other threads access it, choose synchronization for that actual access pattern and document the reason. When volatile read/write semantics are needed and C# supports the modifier for the field's type, prefer a `volatile` declaration with direct access. Use explicit `Volatile` operations when the declaration cannot carry the modifier or when combining a plain field with `Interlocked` operations avoids passing a volatile field by reference. Do not add warning suppressions merely to enforce a volatile declaration.

`volatile` does not make compound operations atomic, protect the referenced object's mutable state, or prevent disposal after a reference is read. Retain the locks and lifetime checks required by shared contexts, transactions, caches, and disposal. Fields whose accesses are already consistently protected by a lock do not need `volatile` solely because multiple threads use them.

## Class member order

Arrange members in this order:

1. Constants and fields.
2. Constructors, immediately after the fields.
3. Ordinary methods.
4. Properties and getter/setter methods, at the end of the class.

The following example demonstrates member order, naming, explicit types, Allman braces, and English documentation comments:

```csharp
using System;

/// <summary>Stores a product name with a maximum supported length.</summary>
public class ProductSettings
{
    private const int MAX_NAME_LENGTH = 200;
    private string fName;

    /// <summary>Initializes the settings with a validated product name.</summary>
    /// <param name="name">The initial product name.</param>
    /// <exception cref="ArgumentNullException">The name is null.</exception>
    /// <exception cref="ArgumentException">The name exceeds the maximum length.</exception>
    public ProductSettings(string name)
    {
        fName = string.Empty;
        setName(name);
    }

    /// <summary>Clears the stored product name.</summary>
    public void ResetName()
    {
        fName = string.Empty;
    }

    /// <summary>Returns the stored product name.</summary>
    /// <returns>The current product name.</returns>
    public string getName()
    {
        return fName;
    }

    /// <summary>Changes the product name after validating its length.</summary>
    /// <param name="name">The new product name.</param>
    /// <exception cref="ArgumentNullException">The name is null.</exception>
    /// <exception cref="ArgumentException">The name exceeds the maximum length.</exception>
    public void setName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length > MAX_NAME_LENGTH)
        {
            throw new ArgumentException("The product name is too long.", nameof(name));
        }

        fName = name;
    }
}
```

## Properties, getters, and setters: no expression bodies

**Do not use `=>` for properties or getter/setter implementations.** Write complete blocks with Allman braces. This includes expression-bodied properties, accessors such as `get => ...` and `set => ...`, and expression-bodied getter/setter methods.

For example, a C# property in a class with an `fName` field uses this form:

```csharp
/// <summary>Gets or changes the stored name.</summary>
public string Name
{
    get
    {
        return fName;
    }
    set
    {
        fName = value;
    }
}
```

Do not use forms such as `public string Name => fName;`, `get => fName;`, or `public string getName() => fName;`.

Auto-properties and interface declarations with `get;` and `set;` remain valid. When an accessor has an implementation, use a full block. Preserve existing accessor visibility and behavior when changing its formatting.

The restriction applies to properties and getter/setter implementations. LINQ lambda expressions, such as `item => item.Price > 0m`, may still use `=>`.

## Documentation comments

Every new method, function, and constructor needs a documentation comment immediately above it, even when it is small. Describe its purpose rather than merely repeating its name.

For C#, use XML documentation:

- `<summary>` for the purpose.
- `<param>` for each parameter.
- `<returns>` when a value is returned.
- `<exception>` for relevant exceptions the caller may receive.

When substantially modifying an existing method or function that lacks documentation, add it. Keep existing documentation consistent with behavior. Use the equivalent documentation format for other languages when applicable.

Inline comments should explain business rules, workarounds, edge cases, technical constraints, or the reason for a decision. Avoid comments that merely restate obvious code. All documentation and inline comments must be in English.

## Framework and public contract compatibility

Preserve names and signatures required by frameworks, SDKs, interfaces, base classes, and external APIs. Overrides and interface implementations keep their required signatures even when those names differ from these conventions.

Do not rename public APIs, serialized or JSON fields, database tables or columns, configuration keys, resources, or external identifiers merely to apply a naming rule. They may be required by reflection, serialization, binding, or integration contracts. Change them only when explicitly requested as part of the task.

## Focused changes

- Preserve existing behavior unless a behavior change is requested.
- Make small, focused changes to the relevant code and documentation.
- Avoid unrelated refactoring, cleanup, formatting, renaming, or optimization.
- Do not change dependencies, project structure, schemas, migrations, or API contracts without a clear task-related reason.
- Explain necessary changes beyond the requested scope before proceeding.
- Report unrelated problems instead of automatically fixing them.
- Explain what changed, which files were affected, and any behavior changes or considerations the user needs to know.

## Blueprint API usage

Applications, demos, manual examples, and consumer tests use the Entity API, such as `Product.Create()`, `Product.Where(...)`, and `Product.Insert(product)`.

Use `Blueprint.Entities` for entity classes and the shared Entity API, and `Blueprint.Entities.Annotations` for their annotations. Keep the SQL persistence implementation in `Blueprint.Database.Sql`.

Declare entity contracts as public, non-sealed classes derived from `BlueprintEntityDB` or `BlueprintEntityRAM`, with `[Entity]` on the class. Abstract classes with public abstract properties are supported; implemented multilingual properties require virtual accessors. Do not introduce the removed `[RAMCached]` annotation. Continue to create entity instances through the Entity API so Blueprint supplies the generated subclass. See [entity classes and migration](Entitites-BlueprintEntity.md).

Direct `BlueprintDatabase.Create<T>()` calls belong to Blueprint's implementation. Queries and writes are accessed through the Entity API; the former `Collection<T>()` helper has been removed. Use the database for configuration and its `TransactionFactory` for transactions. See [EShop - Checkout Tutorial](Database-EShopCheckoutTutorial.md) and [Transactions](Database-Transactions.md).

## Tests and verification

Add tests to `Blueprint.Tests` under a namespace for the feature being tested. Derive from `BlueprintTest`, implement `RunTest()`, and register the test's creation and execution in `Program.cs`. Reuse the shared `fDatabaseConnectionString` configuration for SQL tests.

Test consumer behavior through public APIs. Do not expose implementation details or add public library hooks solely to support tests. Keep temporary test database setup and cleanup isolated from application data.

Run checks appropriate to the change. For documentation examples, verify that the code compiles and that relative links work. Update the relevant manual page when consumer behavior changes.

## Publishing the GitHub Wiki

Maintain the manual in the repository's `.wiki` directory. The `Sync Wiki` GitHub Actions workflow publishes changes pushed to `main`; it can also be started manually from the Actions tab. The GitHub Wiki must already have an initial page. Publishing uses the workflow's built-in `GITHUB_TOKEN` with `contents: write`, without a separate personal access token.

The publishing script maps `README.md` to `Home.md`, converts page links to Wiki URLs, copies images, and links source files back to `main`. It validates local link targets before updating the Wiki. A synchronization manifest tracks published files so removed source pages are removed from the Wiki while independently created pages are preserved. Edits to generated pages should be made in `.wiki`, because publishing replaces those pages.

## Related topics

[Getting Started](GettingStarted.md) · [EShop - Checkout Tutorial](Database-EShopCheckoutTutorial.md) · [Transactions](Database-Transactions.md)
