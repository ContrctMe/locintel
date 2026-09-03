#!/usr/bin/env python3
"""LocIntel module generator (ADR 36): scaffolds a vertical-slice module with
its own schema, DbContext, migration history, and registration checklist.

Usage: python3 tools/new-module.py Bookings
"""
import pathlib
import subprocess
import re
import sys

if len(sys.argv) != 2 or not re.fullmatch(r"[A-Z][A-Za-z0-9]+", sys.argv[1]):
    sys.exit("usage: new-module.py <PascalCaseName>   e.g. new-module.py Bookings")

name = sys.argv[1]
schema = re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()
root = pathlib.Path(__file__).resolve().parent.parent
module_dir = root / "src" / "Modules" / f"LocIntel.Modules.{name}"
if module_dir.exists():
    sys.exit(f"{module_dir} already exists")

(module_dir / "Data").mkdir(parents=True)

(module_dir / f"LocIntel.Modules.{name}.csproj").write_text(f"""<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\\..\\LocIntel.Contracts\\LocIntel.Contracts.csproj" />
    <ProjectReference Include="..\\..\\LocIntel.Platform\\LocIntel.Platform.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" />
    <PackageReference Include="WolverineFx.EntityFrameworkCore" />
    <PackageReference Include="WolverineFx.Http" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
</Project>
""")

(module_dir / "Data" / f"{name}DbContext.cs").write_text(f"""using Microsoft.EntityFrameworkCore;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.{name}.Data;

public sealed class {name}DbContext(DbContextOptions<{name}DbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{{
    public override string ModuleSchema => "{schema}";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {{
        base.OnModelCreating(modelBuilder);
        // Map entities here. Checklist per entity (CLAUDE.md):
        //  - deletion tier (ADR 25)
        //  - temporal kinds on every date/time column (ADR 26/27)
        //  - UUIDv7 keys (ADR 35)
        //  - IOrgScoped for tenant data + EnableTenantRls in the migration
    }}
}}
""")

(module_dir / "Data" / "DesignTimeFactory.cs").write_text(f"""using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.{name}.Data;

/// <summary>Design-time only (dotnet ef). Never used at runtime.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<{name}DbContext>
{{
    public {name}DbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<{name}DbContext>()
                .UseNpgsql("Host=localhost;Database=design_time_only", npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "{schema}"))
                .Options,
            new TenantContext());
}}
""")

(module_dir / f"{name}Module.cs").write_text(f"""using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LocIntel.Modules.{name}.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.{name};

public static class {name}Module
{{
    public static IServiceCollection Add{name}Module(this IServiceCollection services)
    {{
        // persistence is built in ONE place (ModulePersistence, ADR 35): the
        // schema is the only fact a module supplies
        services.AddModuleDbContext<{name}DbContext>("{schema}");
        services.AddScoped<LocIntel.Contracts.IOrgDataExporter, {name}Exporter>();
        return services;
    }}
}}
""")

# The architecture guard requires EVERY module to contribute an org data
# export section (a module without one drops out of offboarding silently),
# so scaffold it rather than leave a generated module failing the build.
(module_dir / "Offboarding.cs").write_text(f"""using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.{name};

/// <summary>
/// {name}'s slice of the offboarding export (ADR 25). Every module ships one:
/// a module without an exporter drops out of an org's data export silently,
/// which is data loss on offboarding. An architecture test enforces it.
/// </summary>
public sealed class {name}Exporter : IOrgDataExporter
{{
    public string Section => "{schema}";

    // TODO: inject {name}DbContext and project this module's rows for the org,
    // using IgnoreQueryFilters() with an explicit OrgId predicate.
    public Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default) =>
        Task.FromResult(
            JsonSerializer.Serialize(
                new {{ }},
                new JsonSerializerOptions(JsonSerializerDefaults.Web) {{ WriteIndented = true }}
            )
        );
}}
""")

def edit(path, anchor, addition, label):
    """Apply a wiring edit, or say precisely what to do by hand."""
    p = root / path
    text = p.read_text()
    if addition.strip() in text:
        print(f"  = {label} (already present)")
        return
    if anchor not in text:
        print(f"  ! {label}: anchor not found - add by hand: {addition.strip()}")
        return
    p.write_text(text.replace(anchor, anchor + addition, 1))
    print(f"  + {label}")

print(f"created {module_dir}")
print("wiring:")
subprocess.run(["dotnet", "sln", "add", f"src/Modules/LocIntel.Modules.{name}"],
               cwd=root, capture_output=True)
print("  + solution")
subprocess.run(["dotnet", "add", "src/LocIntel.Api", "reference",
                f"src/Modules/LocIntel.Modules.{name}"], cwd=root, capture_output=True)
print("  + LocIntel.Api project reference")

edit("src/LocIntel.Api/Program.cs",
     "using LocIntel.Modules.Audit;",
     f"\nusing LocIntel.Modules.{name};",
     "Program.cs using")
edit("src/LocIntel.Api/Program.cs",
     "builder.Services.AddChecklistsModule();",
     f"\nbuilder.Services.Add{name}Module();",
     "Program.cs module registration")
edit("src/LocIntel.Api/Program.cs",
     "    opts.Discovery.IncludeAssembly(typeof(TenancyModule).Assembly);",
     f"\n    opts.Discovery.IncludeAssembly(typeof({name}Module).Assembly);",
     "Program.cs Wolverine discovery")
# formatting-proof anchor: the list terminator, not a formatted entry
catalog = root / "src/LocIntel.Api/ModuleCatalog.cs"
catalog_text = catalog.read_text()
entry = f'        new("{schema}", "{schema}", typeof(LocIntel.Modules.{name}.Data.{name}DbContext)),\n'
if entry.strip() in catalog_text:
    print("  = ModuleCatalog entry (already present)")
elif "\n    ];" in catalog_text:
    catalog.write_text(catalog_text.replace("\n    ];", "\n" + entry + "    ];", 1))
    print("  + ModuleCatalog entry (migrations, grants, RLS coverage, round-trip, fixture)")
else:
    print(f"  ! ModuleCatalog: add by hand: {entry.strip()}")

print(f"""
left to do:
 1. First migration (RLS checklist in the new-migration skill):
    dotnet ef migrations add Initial --project src/Modules/LocIntel.Modules.{name} --startup-project src/Modules/LocIntel.Modules.{name}
 2. Fill in {name}Exporter.ExportJsonAsync (it currently exports an empty object).
 3. If this module owns tenant rows, add a PurgeOrg{name} message + handler and
    publish it from OrgPurgeFanOut, or the rows outlive the org.

Remember (CLAUDE.md): one Wolverine handler class per message; [Transactional(typeof({name}DbContext))]
on endpoints whose chain touches another module's DbContext (injecting IScopeResolver counts).
The catalog entry is what makes migrations, grants, RLS coverage, round-trips and
the fixture pick this module up - an architecture test fails if it is missing.""")
