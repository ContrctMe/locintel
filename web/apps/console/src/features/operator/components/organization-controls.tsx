import { ENTITLEMENTS, type EntitlementCode, type components } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, Input } from '@locintel/ui';
import { useState } from 'react';
import { useSessionTransition } from '../../../app/session-boundary';
import { entitlementLabel } from '../../../lib/format';
import { useApiMutation } from '../../../lib/mutation';
import { operatorApi } from '../api';
import { useOperatorEntitlements } from '../hooks';
import { parseEntitlementValue } from '../schema';

type OperatedOrg = components['schemas']['OperatedOrgResponse'];

/** Owns actions and drafts for one selected organization; callers key by org id. */
export function OrganizationControls({ org, onOffboard }: {
  org: OperatedOrg;
  onOffboard: () => void;
}) {
  const transition = useApiMutation({
    mutationFn: (input: { orgId: string; action: 'suspend' | 'reactivate' }) =>
      operatorApi.transition(input.orgId, input.action),
    invalidate: [['operator-orgs']],
    success: 'Status updated',
  });
  const exportOrg = useApiMutation({
    mutationFn: operatorApi.exportOrg,
    success: "Export queued - it lands in the org's Files",
  });
  const changeSession = useSessionTransition();
  const offboard = useApiMutation({
    mutationFn: operatorApi.offboard,
    invalidate: [['operator-orgs']],
    success: 'Offboarding started',
    onSuccess: onOffboard,
  });
  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center justify-between">
            {org.name}
            <span className="flex items-center gap-2">
            {org.status === 'Active' && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => void changeSession(() => operatorApi.impersonate(org.id))}
              >
                Impersonate
              </Button>
            )}
            {org.status === 'Active' ? (
              <Button
                variant="destructive"
                size="sm"
                disabled={transition.isPending}
                onClick={() => transition.mutate({ orgId: org.id, action: 'suspend' })}
              >
                Suspend
              </Button>
            ) : (
              <Button
                size="sm"
                disabled={transition.isPending}
                onClick={() => transition.mutate({ orgId: org.id, action: 'reactivate' })}
              >
                Reactivate
              </Button>
            )}
            </span>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <OrgEntitlements orgId={org.id} />
        </CardContent>
      </Card>
      <Card>
        <CardHeader><CardTitle>Lifecycle</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={exportOrg.isPending}
              onClick={() => exportOrg.mutate(org.id)}
            >
              Export data
            </Button>
            <span className="text-sm text-muted-foreground">
              {exportOrg.isSuccess
                ? "Queued - the archive lands in the org's Files."
                : 'Full data archive, delivered to the org’s file library.'}
            </span>
          </div>
          <div className="flex items-center gap-2">
            <ConfirmButton
              variant="destructive"
              size="sm"
              confirmLabel="Purge org data?"
              disabled={offboard.isPending || org.status !== 'Suspended'}
              onConfirm={() => offboard.mutate(org.id)}
            >
              Offboard
            </ConfirmButton>
            <span className="text-sm text-muted-foreground">
              {org.status === 'Suspended'
                ? 'Purges all org data. The audit trail and org record remain.'
                : 'Suspend the org first - offboarding is a deliberate two-step.'}
            </span>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

function OrgEntitlements({ orgId }: { orgId: string }) {
  const { data: effective } = useOperatorEntitlements(orgId);
  const [drafts, setDrafts] = useState<Record<string, string>>({});

  const set = useApiMutation({
    mutationFn: (input: { code: string; value: string }) =>
      operatorApi.setEntitlement(orgId, input.code, parseEntitlementValue(input.value)),
    invalidate: [['operator-entitlements', orgId]],
    success: 'Entitlement updated',
    errorFallback: 'Update failed',
  });

  return (
    <div className="space-y-2">
      {effective &&
        (Object.keys(ENTITLEMENTS) as EntitlementCode[]).map((code) => {
          const draft = drafts[code] ?? effective[code]?.value ?? '';
          const dirty = draft !== effective[code]?.value;
          return (
            <div key={code} className="flex items-center gap-2 text-sm">
              <span className="w-56 text-muted-foreground" title={code}>
                {entitlementLabel(code)}
              </span>
              <Input
                aria-label={entitlementLabel(code)}
                className="h-8 w-32"
                value={draft}
                onChange={(e) => setDrafts({ ...drafts, [code]: e.target.value })}
              />
              {dirty && (
                <Button size="sm" disabled={set.isPending}
                  onClick={() => set.mutate({ code, value: draft })}>
                  Save
                </Button>
              )}
            </div>
          );
        })}
    </div>
  );
}
