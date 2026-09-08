# Engineering maintenance

## Shared imports and lookups

CSV parsing lives in `Platform.Text.CsvParser`; each caller supplies byte, row,
column and field limits. Site ingest retains its existing 4 MiB / 10,000-row /
16-column / 500-character limits. Read bytes through `BoundedRead` before
parsing. Object storage I/O finishes before upload staging starts its database
transaction; staging writes and the organization lock remain atomic.

`IStoredFileLookup.GetManyAsync` reads only requested, tenant-visible,
non-derived files, in chunks of 1,000 distinct IDs. Missing IDs are omitted;
it does not grant download access. Consumers still authorize the parent record.

`GET /api/sites?ids=<comma-separated UUIDs>` applies the same tenant and scope
filters as the normal list, with at most 200 input IDs. The console's metadata
hook uses 50 IDs per request rather than loading a tenant's entire site catalog.
`SitePicker` searches the server; `FilePicker` follows file-list pagination and
keeps later pages reachable even if the current page has no eligible files.

## Reproducible checks

Integration tests run at most two test collections concurrently. Each owns a
real database and API host. CI still splits classes across two independent
jobs. Avoid concurrently running the full browser stack on the same Docker
host; resource contention can turn a useful failure into a timeout.

Build, generate static handlers, then rebuild before a complete integration run:

```sh
dotnet build LocIntel.slnx
ASPNETCORE_ENVIRONMENT=Testing ROLE=api ConnectionStrings__locintel='Host=localhost;Port=1;Database=x;Username=x;Password=x' dotnet run --project src/LocIntel.Api --no-build --no-launch-profile -- codegen write
dotnet build tests/LocIntel.IntegrationTests --no-restore
LOCINTEL_TEST_SHUFFLE=20260908 dotnet test tests/LocIntel.IntegrationTests --no-build --logger trx
```

Run `tools/e2e-stack.sh` separately for the browser matrix. An isolated rerun
passing does not establish that a complete suite passes. Record the revision,
shuffle seed, command, failure count and any skipped checks with the result.

## Fork synchronization

Use a canonical upstream repository URL for the `template` remote, not another
developer's checkout. `tools/init.py` preserves its clone source; a fork cloned
from a local path must deliberately replace that path with the upstream URL:

```sh
git remote set-url template <upstream-url>
git fetch template
tools/sync-upstream.sh
```

Never rebase or squash `template-renamed`. Snapshot commits record the complete
source SHA in `Template-Commit`; the script rejects snapshots that do not include
that source revision. If testing an upstream feature branch, continue syncing
that branch until it is merged upstream. This prevents a later sync of an older
`main` from silently removing the tested changes.

Prefer upstream structure during conflicts, but retain product behavior through
explicit acceptance tests. Check authentication/session transitions, capability
registrations, storage lifecycle, product routes and generated contracts. Keep
an inventory of intentional platform differences in the product repository.

The fork regression syncs twice and checks that coverage, load tools,
infrastructure and browser manifests use the fork's name. These files are part
of the product, not incidental template text.
