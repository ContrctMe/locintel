# Public session recovery

- Status: unit and focused three-browser verification passed; full suite pending
- Scope: public locator sign-out through the SSR host
- Updated: 2026-09-05

## Contract

The browser calls the public server function; that server forwards the HttpOnly
session cookie to the API's logout endpoint. The API revokes the server session
and returns cookie deletions. Only a 204 response with cookie-deletion headers
is treated as confirmed success, and deletions are relayed onto the public host.
No authentication token is exposed to JavaScript (ADR 21).

Network failure, non-204 responses, missing deletion headers, and failure of the
browser-to-SSR hop must not silently report success. The page displays an alert
that sign-out could not be confirmed and the session may still be active. It
retains the current view and permits retry; the button is disabled during the
request. It refreshes loader data only after confirmed success.

This is not offline revocation. Removing a local cookie alone cannot prove that
the server session was revoked. A lost response can also mean the API completed
logout but its acknowledgment was lost; the UI deliberately reports uncertainty.
No automatic write retry or change to cookie domain/security policy is introduced.

## Verification

`pnpm -C web --filter @locintel/public test` covers upstream network failure,
HTTP failure, missing deletion headers, and successful cookie forwarding and
deletion relay. The three failure cases reproduced false success before the fix.

`tools/e2e-stack.sh --project=chromium --grep 'public logout'` exercises real
contact-link issuance/redemption, browser-to-SSR interruption, visible failure,
retry, cookie removal, and signed-out state after reload. Focused Chromium passed
1/1 (3.1s). The initial test also counted the setup owner's localhost cookie;
the contact visitor is now isolated before redemption. Its original trace is
preserved in `/tmp/locintel-public-logout.XIXIJ4/`.

The subsequent focused run passes logout, locator rendering, and checklist
selection in all three engines: 9/9 (Chromium 8.1s, Firefox 10.9s, WebKit 8.8s),
normal exit and no retries. Frontend units pass 29 console + 11 public; application typecheck, lint, builds,
and diff checks pass. Full browser-matrix and live domain/proxy/provider
acceptance remain separate. A direct e2e-package typecheck exposed pre-existing
configuration failures (missing `web/tools/tsconfig.base.json` and Node type
resolution). Both are now repaired, and the e2e typecheck script participates
in the existing recursive workspace/CI command, which passes. The subsequent
full browser run finished 119/120: an existing WebKit tenant-switch assertion
failed on a 5.03-second site read. Logout passes all engines; the overall suite
remained open pending investigation. Following the native-cancellation test
repair and fresh-login budget isolation, the combined matrix now passes
120/120 across all three engines. Historical latency is not declared fixed;
details and remaining deployment acceptance are in the maturity review.

See the [maturity review](software-maturity-review-details.md) for the remaining
ordered findings and [production guide](production.md) for deployment policy.
