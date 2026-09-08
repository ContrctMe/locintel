# Checklist site selection and read recovery

- Status: implemented; focused three-browser verification passed
- Updated: 2026-09-05

The checklist picker reuses the sites feature's public `useSites` hook. It loads
50 authorized sites at a time and follows the API's `nextOffset` through an
explicit Load more sites button. It no longer silently stops at 200 sites.
Additional pages preserve the selected site; a failed next page offers retry
without discarding the existing selection. No server quota or page-size limit
is raised and no unbounded initial fetch is introduced.

Sites, today's checklists, and templates each expose loading and error/retry
states. A successful empty response remains distinct from a failed read.
Checklist network calls live in the checklist feature API; the route imports
only its public entry point. The existing server validates mutation payloads.

## Verification and limits

`tools/e2e-stack.sh --grep 'checklist picker'` uses real authentication and
controlled read responses with 201 sites. It checks offsets 0/50/100/150/200,
selection of site 201, initial and next-page site failures, selection retention,
and checklist/template read recovery. Focused Chromium passed 1/1 (5.7s).
The combined focused run passed all three flows (checklist selection, public
SSR rendering, and logout recovery) in Chromium, Firefox, and WebKit: 9/9,
normal exit, no retries. This is UI behavior evidence, not real-database
cardinality or isolation proof.
The added FleetPagingTests separately passes on real PostgreSQL with 201 sites:
exactly five pages, no duplicates or missing IDs, and no rows visible to another
tenant. Only admin setup bypasses query filters/provisioning quotas; HTTP reads
retain app_user and RLS. This is not a load test.
The existing backend PagingTests separately exercise pagination/search and scope;
they have not been rerun for this frontend-only change.

The native select accumulates explicitly requested pages. Very large fleets may
justify a searchable picker, but fetching the entire fleet automatically is not
the fallback. The subsequent complete browser matrix passes 120/120 across
Chromium, Firefox, and WebKit, including this picker test. Historical
intermittent latency remains tracked in the maturity review.

See the [maturity review](software-maturity-review-details.md) for the full goal.
