# Small DigitalOcean compatibility deployment

This is the private bootstrap slice of the [deployment plan](../../docs/digitalocean-test-environment.md).
Status: Terraform schema/mock-plan and local application keyring checks pass;
**no cloud resources have been provisioned and hosted compatibility is unverified**.

Local verification on 2026-09-07: Terraform validation and one mocked topology
test passed; the manifest renderer self-check passed. The latest full integration
run passed 321 tests (including certificate/keyring, Production boot guards and
capacity reservations), with one expected opt-in scale skip. The pre-generated
`linux/amd64` image passed `tools/smoke-image.sh`: migrations, non-root API/worker
readiness, durable worker cleanup and log/dead-letter checks. Its local image ID
is `sha256:9f18553fd4556d74439119b3246dd51b27d1fb148ac62c0c31de8aa5b0d62e3c`
(not a registry digest). Temporary smoke containers were removed. These local
checks do not exercise managed PostgreSQL privileges, NFS or real providers.

## Scope and cost

Two fixed `c-4` DOKS nodes, no HA control plane, PostgreSQL 17 on one
`gd-2vcpu-8gb` node with 30 GiB storage, private VPC and database firewall,
50 GiB **Standard** NFS, and an isolated campaign project. The private application
slice has two anti-affine APIs and one worker. This smaller topology tests
compatibility, not the final capacity or HA target. There is no load generator,
public load balancer, automatic expiry cleaner, frontend or gateway deployment
in this slice. Do not advertise it as production-qualified.

Using the verified prices in the parent plan: about $0.42/hour for compute and
PostgreSQL ($10.05 for 24 hours), plus NFS and external provider/storage charges.
Allow $20 for the first 24-hour infrastructure slice, excluding unknown provider
subscriptions. The expiry is inventory metadata; it does not delete resources.
Provision only when an operator can complete cleanup within that window.

## Before applying

### Ordered setup checklist

Complete steps 1–6 before starting billable cluster resources. Accounts and test
domains can be reused between campaigns; the cluster, database, NFS share and
synthetic data are disposable. The maintainer supplies account/environment choices
and protected credential locations; the deployment operator performs provisioning,
configuration, validation and teardown.

1. **Select account, region and campaign bounds.** Confirm the DigitalOcean team
   and CLI context (`kajay-dev` is currently active, not yet selected). Use a new
   campaign project, not the existing `kajay` project. Confirm billing, resource
   quotas, region support for all three services, an attended test window, expense
   allowance and teardown owner. A project is organizational grouping, not an
   account-level security boundary.
2. **Prepare operator access and evidence storage.** Provide a DigitalOcean API
   token for Terraform with the required project/VPC/tag/Kubernetes/database/NFS
   lifecycle permissions. Use a protected campaign directory with private file
   permissions for state, plans, credentials and kubeconfig, and a backed-up
   evidence location outside the disposable resources. Required local tools are
   Terraform 1.12+, doctl, kubectl, Docker, .NET 10, Python 3 and OpenSSL.
   Node/pnpm are needed when building the frontends. Do not paste credentials into
   chat; provide their approved local file or secret-manager locations.
3. **Select the container registry.** Create/select a private registry and image
   repository with publisher and Kubernetes pull access. Build with the exact
   pre-generation/publish sequence below, smoke it, push it, and record its
   registry digest. The existing local image is not yet a published artifact.
4. **Reserve test names and identities.** Choose a domain you control and reserve
   `console.<test-domain>`, `api.<test-domain>` and tenant names matching
   `https://{slug}.<test-domain>`. Choose an owned mailbox and synthetic users/orgs
   for tests. DNS records wait for the actual gateway address. These names must
   agree across callbacks, redirects, CORS, mail links and frontend configuration.
5. **Set up real provider test environments.** Collect the exact configuration in
   the table below. Use test/sandbox billing and synthetic users. Create the
   accounts and credentials now; activate webhook endpoints after HTTPS routing
   exists. Verify the selected mail provider offers STARTTLS on a usable port.
6. **Prepare scanner and test access deployment.** Select a real ClamAV daemon
   with current signatures, a private service on TCP 3310, and enough measured
   memory; its Kubernetes deployment is still to be added. Prepare gateway/TLS,
   console and public SSR manifests before attempting browser/provider tests.
   The current renderer contains none of these. Private health/database/keyring
   checks can precede them; browser login, provider webhooks and public links cannot
   be declared verified using the private backend alone.
7. **Provision infrastructure.** Validate/mock-test Terraform and the renderer.
   Copy the Terraform definition and lock file into the protected campaign
   directory, initialize it, supply a current tfvars file, inspect a saved plan,
   then apply it. Save inventory and obtain a dedicated kubeconfig. Verify the two
   nodes, private database routing and firewall before deploying the app.
8. **Prepare runtime secrets and storage.** Create the campaign namespace first.
   Retrieve the actual database CA and admin connection details; generate the
   separate app-user password. Generate a campaign PFX with private key and mount
   it for all replicas. Verify NFS support from both nodes and restricted export
   ownership for UID/GID 1654. Install the six Kubernetes Secrets listed below.
   Database/app secrets must stay separate; NFS must work before the migration
   Job starts because the Job also mounts it. Start/verify the scanner.
9. **Run migration, then serving roles.** Apply only the migration phase and wait
   for success. Inspect managed-database extension, ownership and RLS behavior;
   stop on incompatible privileges rather than granting runtime bypass access.
   Then apply workloads, confirm API replicas occupy distinct nodes, and verify
   readiness, durable worker processing and cross-replica key decryption.
10. **Connect the real application flow.** Deploy the production-configured
    gateway and frontends; install TLS, set DNS and trusted proxy handling, and
    preserve private API/identity access. Register the exact WorkOS callback and
    webhook URLs and Stripe webhook destination. Configure storage CORS for the
    console origin. Verify source-IP handling and rejection of forged gateway
    headers before admitting public test traffic. This is remaining implementation
    work, not a capability of `render.py` today.
11. **Run compatibility acceptance and export evidence.** Follow the gates below:
    tenant isolation, pod replacement/session continuity, real provider operations,
    clean/infected upload handling, and durable work. Include managed audit
    partition maintenance in database verification. Preserve sanitized results,
    configuration/source/image identifiers and resource inventory. This small
    topology does not establish 1,000 requests/second or HA.
12. **Destroy and reconcile.** Remove campaign Kubernetes resources while the
    cluster exists, then apply the reviewed Terraform destroy plan. Check every
    inventory ID and any manually added storage, DNS, test data and provider
    resources. Keep evidence and shared provider accounts; delete campaign secrets
    only after their protected test data is no longer needed. Expiry metadata
    does not perform any of these actions automatically.

| Provider/setup | Required configuration and ordered details |
|---|---|
| WorkOS test environment | `Auth__Provider=workos`, `Auth__WorkOS__ApiKey`, `Auth__WorkOS__ClientId`. Register `https://console.<test-domain>/auth/callback` once the console host routes auth to the API. Configure `https://api.<test-domain>/auth/directory/webhook`, its required `Auth__WorkOS__WebhookSecret`, and `session.revoked`, `password_reset.succeeded`, `user.deleted`. Add `dsync.user.created`, `dsync.user.updated`, `dsync.user.deleted` for directory sync. Verify signed session revocation delivery; provision a test directory if testing directory revocation. |
| Stripe test environment | `Billing__Provider=stripe`, `Billing__Stripe__ApiKey`; create prices and set `Billing__Stripe__PriceIds__growth` and `Billing__Stripe__PriceIds__scale`. Register `https://api.<test-domain>/billing/webhook` for `checkout.session.completed`, `customer.subscription.updated`, `customer.subscription.deleted`, then use that destination's `Billing__Stripe__WebhookSecret`. |
| SMTP provider | `Notifications__Transport=smtp`, `Notifications__Smtp__Host`, `__Port`, `__UserName`, `__Password`, `__FromAddress`, optionally `__FromName`; keep `Notifications__Smtp__UseStartTls=true`. Verify the sender/domain and publish the provider's SPF/DKIM records and a DMARC policy. Verify actual delivery to the owned test mailbox from a pod. |
| Spaces/S3 object storage | Dedicated private test bucket and bucket-scoped object credentials. Set `Storage__Provider=s3`, `Storage__S3__BucketName`, `Storage__S3__ServiceUrl`, `Storage__S3__AccessKey`, `Storage__S3__SecretKey`. For Spaces use its regional HTTPS endpoint. Configure console-origin CORS for PUT/GET and `Content-Type`/`If-None-Match`. Test create-only upload, metadata, download and deletion against the actual provider. |
| AWS KMS, if selected | Symmetric encryption key and an application identity authorized for `kms:Encrypt`/`kms:Decrypt` on that key through both IAM and key policy. Set `Secrets__Provider=kms`, `Secrets__Kms__KeyId`, `AWS_REGION` and credentials available through the AWS SDK chain. Temporary credentials also require their session token and a lifetime covering the run. Leave the emulator `ServiceUrl` unset. A DO-only replacement adapter is not implemented. |
| ClamAV | `Scanner__Provider=clamav`, `Scanner__ClamAv__Host`, `Scanner__ClamAv__Port=3310`. Keep the scanner private; verify signature availability and successful scans before upload acceptance. |
| Shared application configuration | `Database__AppUser=app_user`, `Public__HostTemplate=https://{slug}.<test-domain>`, a random `Gateway__IdentityKey` meeting the app's 32-byte minimum, optional PFX password in `DataProtection__CertificatePassword`, and a recorded `Build__Version`. The renderer supplies role, Production mode, certificate/key paths and gateway-required settings. |

DigitalOcean documents outbound SMTP ports **25, 465 and 587 as blocked on
Droplets**. Do not assume the default SMTP port works from DOKS workers. Select a
provider-supported alternative port (for example 2525, if offered) and verify
STARTTLS connectivity from an actual pod. An HTTPS email API would require a
different transport adapter; it is not a configuration switch in the current
SMTP transport. See [DigitalOcean's SMTP policy](https://docs.digitalocean.com/support/why-is-smtp-blocked/).

Provider references: [WorkOS authorization redirects](https://workos.com/docs/reference/authkit/authentication/get-authorization-url)
and [Spaces API/endpoint reference](https://docs.digitalocean.com/reference/api/spaces/).

Resolve the account/context and real provider configuration first. Read-only
inspection found `kajay-dev` active, but the maintainer has not selected it for
this deployment. AWS KMS versus an alternative production wrapper is also pending.
No credentials belong in committed files or conversation messages.

Verify that the selected region offers the exact cluster plan, database and
Standard NFS. The CLI listed `c-4`, `gd-2vcpu-8gb` and Kubernetes `1.36.3-do.3`;
that inventory does not prove every combination is available in NYC3. NFS must
be mountable by DOKS workers, with its export writable by UID/GID 1654. Validate
NFS client support and set restricted ownership on the campaign share before
starting the application; do not fix permissions with world-writable keys.

Use a separate protected working directory for each campaign's Terraform state,
plan, credentials and kubeconfig. State includes provider-generated secrets even
though `inventory` output is non-secret. Remote state with locking is required
before concurrent/CI operation; a private backed-up local state is sufficient
for this single-operator compatibility rehearsal. Do not lose state on teardown.

```bash
# Read-only/local validation: no account needed; mocked tests do not provision.
terraform -chdir=deploy/digitalocean init -backend=false
terraform -chdir=deploy/digitalocean validate
terraform -chdir=deploy/digitalocean test
python3 deploy/digitalocean/render.py --check
```

For the actual campaign, copy `main.tf` and `.terraform.lock.hcl` into its private
working directory, supply an explicitly selected token through
`DIGITALOCEAN_TOKEN`, and use a private tfvars file based on
`compat.tfvars.example`. Set a unique valid campaign name and a future expiry
within 24 hours. Run `terraform plan -out=compat.tfplan`, review the exact account,
resources and total, then apply that saved plan. Never use the example's placeholder
name or stale expiry. Do not set `TF_LOG`: provider debug logs can contain secrets.

After apply, save `terraform output -json inventory` to `inventory.json`. Obtain
a campaign-only kubeconfig with `doctl kubernetes cluster kubeconfig show <id>`
into a private file; use `kubectl --kubeconfig /private/path/kubeconfig` throughout.
Do not change the user's default Kubernetes context. All kubectl commands below
assume that explicit kubeconfig and the namespace named by `inventory.campaign`.

## Production configuration and secrets

Build/publish the current .NET application for `linux/amd64` using its existing
SDK container publisher and the same pre-generation step as CI:

```bash
dotnet build src/LocIntel.Api -c Release
ConnectionStrings__locintel='Host=localhost;Port=1;Database=x;Username=x;Password=x' \
  ASPNETCORE_ENVIRONMENT=Testing ROLE=api \
  dotnet run --project src/LocIntel.Api -c Release --no-build -- codegen write
dotnet publish src/LocIntel.Api -c Release --os linux --arch x64 \
  -p:PublishProfile=DefaultContainer -p:ContainerImageTag=compat-dev
tools/smoke-image.sh locintel:compat-dev
```

The generation command does not contact a database. Omitting it exposed a
runtime fallback attempting to write generated source under non-writable
`/app/Internal`; the image smoke rejects that error. Publish the verified image
to the selected registry and deploy its immutable registry digest, not the local
image ID. Registry selection and credentials are still required. API and worker
never receive the migration/admin secret.

Create the namespace and these native Kubernetes Secrets from protected files:

| Secret | Contents |
|---|---|
| `registry` | Standard `kubernetes.io/dockerconfigjson` pull credentials for the selected registry |
| `providers` | Production provider configuration below, a randomly generated shared `Gateway__IdentityKey`, and `Database__AppUser=app_user` |
| `app-database` | `ConnectionStrings__locintel` with app_user credentials; `Database__AppPassword` matching that role |
| `migration-database` | `ConnectionStrings__locintel` with provider admin credentials; the same `Database__AppPassword` for the role the migration provisions |
| `database-ca` | `ca.crt` from the actual managed PostgreSQL cluster |
| `keyring-certificate` | `keyring.pfx`, a campaign-specific certificate **with private key**, shared by every API/worker |

Both connection strings use the inventory's private host/port, database `locintel`,
`SSL Mode=VerifyFull;Root Certificate=/database-ca/ca.crt;Maximum Pool Size=20`.
Configure the separate messaging connection only if needed; it must also use
verified TLS and runtime credentials. This phase uses direct connections.

The `providers` secret must use real sandbox/test accounts for WorkOS, Stripe,
SMTP, S3-compatible storage and AWS KMS (if selected), with the keys documented
in [production configuration](../../docs/production.md). Set `AWS_REGION` for KMS;
use approved credentials available to the AWS SDK. Set the intended
`Public__HostTemplate`, mail sender, Stripe price IDs and WorkOS callback details.
Select `Scanner__Provider=clamav` and a real reachable scanner endpoint. An in-cluster
scanner or an existing sandbox scanner still needs to be installed/selected;
this renderer does not pretend a placeholder hostname is a passing scanner test.

The mounted PFX can be protected with `DataProtection__CertificatePassword` in
`providers`. Its encryption protects Data Protection keys on NFS; the business
`Secrets__Provider=kms` setting is a different operation. Keep the PFX/certificate
available for the lifetime of protected cookies/tokens. Historical certificate
rotation is not implemented or qualified by this bootstrap; do not replace the
certificate and discard the old one while those keys are in use.

Using files avoids putting secret values into shell arguments:

```bash
# Run with the explicit campaign kubeconfig/namespace options above.
kubectl create secret generic providers --from-env-file=/private/path/providers.env
kubectl create secret generic app-database --from-env-file=/private/path/app-database.env
kubectl create secret generic migration-database --from-env-file=/private/path/migration-database.env
kubectl create secret generic database-ca --from-file=ca.crt=/private/path/ca.crt
kubectl create secret generic keyring-certificate --from-file=keyring.pfx=/private/path/keyring.pfx
```

These are initial-creation commands, not a blanket secret rotation operation.
Reject inherited `ConnectionStrings__*` or `Database__AppPassword` in `providers`:
only the role-specific database secret owns those values.

## Network and pod security configuration

The renderer installs namespace default-deny ingress/egress, allows API ingress
only from same-namespace pods labeled `app=gateway`, and permits DNS to
`kube-system`/`k8s-app=kube-dns` plus scanner traffic to same-namespace
`app=scanner` on TCP 3310. Only api/worker/migrate receive the additional approved
database/provider egress rules. New gateway, limiter, scanner-signature-update
and observability deployments must bring their own explicit policies; adding a
pod to this namespace does not grant network access. Kubelet probes use the
fixed Host `health.locintel.internal`, included by the renderer.

Supply `--egress-rules /private/path/egress.json`, a nonempty JSON array of native
NetworkPolicy egress rules. Each rule must contain explicit IP CIDRs and TCP/UDP
ports; wildcard destinations, empty peer/port lists and unrestricted CIDRs are
refused. For example, this **illustrative** rule allows one database IP/port:

```json
[{"to":[{"ipBlock":{"cidr":"10.0.0.3/32"}}],"ports":[{"protocol":"TCP","port":25060}]}]
```

Replace it with actual approved database, SMTP, HTTPS-provider and collector
addresses/ports. Record who updates their ranges. If a provider's addresses are
dynamic, use the cluster's supported DNS-aware egress policy or a controlled
outbound proxy with equivalent destination restrictions; do not reopen all
internet egress to make a test pass. Native NetworkPolicy cannot match DNS
names. Confirm CNI enforcement and DNS routing on the selected DOKS cluster.

Set `APP_HOSTS` to the actual semicolon-separated console/API/tenant host names
or suffix wildcards, and `GATEWAY_CIDR` to the actual bounded network of the
immediate gateway peers. Pass them through `--allowed-hosts` and `--proxy-cidr`.
They configure `AllowedHosts`, one-hop forwarding and `Proxy:KnownNetworks:0`;
there is no trust-all fallback. The gateway must strip client forwarding headers.
An unrelated pod/namespace must fail to reach API/identity/DB; verify this on the
real cluster before admitting external traffic.

Pods explicitly request RuntimeDefault seccomp, non-root UID/GID, no privilege
escalation, no capabilities, no service-account token, and a read-only root
filesystem. Only the shared key volume and a bounded 256 MiB `/tmp` volume are
writable. Account for concurrent temporary work: a bounded upload relay can
consume up to 100 MiB per request, so two such requests nearly exhaust this
volume before report/native-library scratch space. Set relay concurrency and
pod ephemeral-storage capacity from measured combined use; exceeding the
volume must fail cleanly rather than increasing it without a budget.

The namespace enforces the baseline Pod Security Standard pinned to v1.36.
The stronger restricted standard does not allow this bootstrap's inline NFS
volume; switch to the managed CSI/PVC mount and validate before enabling it.
Neither a namespace policy nor encrypted key XML secures arbitrary NFS writes:
restrict export/node/VPC access and UID/GID ownership independently, and prove an
unrelated workload cannot access the key share. NFS mounts originate at the node,
so pod egress rules alone are insufficient.

## Deploy and verify in order

Render each phase separately. Rendering contains secret **references**, not values. Apply namespace/network policies in both phases so isolation exists before the migration Job starts.

```bash
python3 deploy/digitalocean/render.py --inventory /private/path/inventory.json \
  --image registry.example/locintel@sha256:REPLACE --egress-rules /private/path/egress.json \
  --allowed-hosts "$APP_HOSTS" --proxy-cidr "$GATEWAY_CIDR" --phase migrate > /private/path/migrate.json
# kubectl [explicit context/namespace] apply -f /private/path/migrate.json
# kubectl [explicit context/namespace] wait --for=condition=complete job/migrate --timeout=600s

python3 deploy/digitalocean/render.py --inventory /private/path/inventory.json \
  --image registry.example/locintel@sha256:REPLACE --egress-rules /private/path/egress.json \
  --allowed-hosts "$APP_HOSTS" --proxy-cidr "$GATEWAY_CIDR" --phase workloads > /private/path/workloads.json
# Apply workloads ONLY after migration succeeds.
# Wait for both API replicas and the worker to become ready; inspect pod node placement.
```

No automatic retry of a failed migration Job is hidden in the deployment. Inspect
its failure; delete/recreate only the campaign Job when intentionally retrying.
The private two-node slice uses `Recreate` for upgrades because a third anti-affine
API cannot fit. This is explicitly not the production rolling-update strategy.

Hosted acceptance still requires execution and retained evidence:

1. All module migrations finish using the managed admin role; verify ltree,
   PostGIS and diagnostic-extension access. Runtime app_user must have neither
   superuser nor BYPASSRLS. Exercise cross-tenant reads and rejected writes through
   the real runtime role, not just metadata inspection.
2. Two APIs run on distinct nodes; the worker completes a durable operation.
   Observe health via Kubernetes port-forward without publicly exposing the API.
3. NFS keys contain encrypted XML; protect data on one instance, unprotect it on
   another and after pod replacement. Exercise real cookie/session and contact-link
   behavior once provider login and private test access are configured.
4. WorkOS login/revocation, Stripe test webhook, owned-recipient SMTP delivery,
   KMS wrap/unwrap and Spaces upload/download/quarantine/scanner checks pass.
   Production-mode startup alone does not establish provider compatibility.
5. Export results and source/image hashes, then rehearse full destroy. Only after
   these gates pass proceed to public TLS/PROXY-protocol gateway deployment and
   its spoofing, fairness and SSR tests in the next slice.

The existing local fleet fixture uses development sign-in; do not run it unchanged
against this production-mode deployment. Hosted fixture preparation remains part
of the provider bootstrap work once the actual accounts are selected.

## Cleanup

Export evidence outside the campaign's state/resources. Remove only the campaign
namespace (and any added Services/PVCs while DOKS still runs), then run a saved
Terraform destroy plan from the same protected state. Verify the inventory IDs
for the cluster/nodes, PostgreSQL, NFS, VPC and project are gone. NFS and VPC may
not appear in the project's resource list: the Terraform inventory is authoritative.
Reconcile manually added scanners, provider test data, buckets, keys and images
separately; Terraform cannot remove resources it never managed. Keep the state
backup until that reconciliation passes. Do not delete reusable provider accounts,
registry, evidence storage or an unrelated project's resources.

An expiry label alone is not an independent cleanup job. Until that job exists,
the first compatibility run needs an attended teardown; do not launch an unattended
campaign and describe it as self-cleaning.
