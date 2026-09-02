#!/usr/bin/env python3
"""LocIntel module generator (ADR 36): scaffolds a vertical-slice module with
its own schema, DbContext, migration history, and registration checklist.

Usage: python3 tools/new-module.py Bookings
"""
import pathlib
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
        services.AddDbContextWithWolverineIntegration<{name}DbContext>((sp, options) =>
        {{
            // Options are SINGLETON: never resolve scoped services here (dev
            // scope-validation rejects it). v1 is single-region (ADR 35);
            // multi-region moves connection selection to a per-scope interceptor.
            var regions = sp.GetRequiredService<IRegionDataSources>();
            options
                .UseNpgsql(regions.For(RegionId.Default), npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "{schema}"))
                .AddInterceptors(
                    TenantSessionInterceptor.Instance,
                    sp.GetRequiredService<LocIntel.Platform.Audit.AuditSaveChangesInterceptor>());
        }});
        return services;
    }}
}}
""")

print(f"""created {module_dir}

Finish the wiring (each is one line):
 1. dotnet sln add src/Modules/LocIntel.Modules.{name}
 2. dotnet add src/LocIntel.Api reference src/Modules/LocIntel.Modules.{name}
 3. Program.cs:  builder.Services.Add{name}Module();
 4. Program.cs:  opts.Discovery.IncludeAssembly(typeof({name}Module).Assembly);
 5. tests/LocIntel.ArchitectureTests ModuleBoundaryTests: add typeof(Modules.{name}.{name}Module).Assembly
 6. src/LocIntel.Api/MigrationRunner.cs: migrate {name}DbContext + add "{schema}" to BOTH grant lines
 7. tests ApiFixture: migrate {name}DbContext + GRANT on schema "{schema}"
 8. First migration (RLS checklist in the new-migration skill):
    dotnet ef migrations add Initial --project src/Modules/LocIntel.Modules.{name} --startup-project src/Modules/LocIntel.Modules.{name}

Remember (CLAUDE.md): one Wolverine handler class per message; [Transactional(typeof({name}DbContext))]
on endpoints whose chain touches another module's DbContext (injecting IScopeResolver counts).""")
