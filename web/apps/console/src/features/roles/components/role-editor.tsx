import type { components } from '@locintel/api';
import { Button, Checkbox, Field, FieldLabel, FieldLegend, FieldSet, Input } from '@locintel/ui';
import { useState } from 'react';
import { useApiMutation } from '../../../lib/mutation';
import { rolesApi } from '../api';
import { CAPABILITY_GROUPS, CAPABILITY_LABELS } from '../labels';
import { GRANTABLE, grantKey, parseGrant } from '../schema';

type Role = components['schemas']['RoleResponse'];
const WILDCARD = '*:*';

/** Draft and save lifecycle are local to this mounted create/edit form. */
export function RoleEditor({ role, onSaved }: { role: Role | null; onSaved: () => void }) {
  const [name, setName] = useState(role?.name ?? '');
  const [picked, setPicked] = useState(() => new Set(role?.grants.map(grantKey) ?? []));
  const save = useApiMutation({
    mutationFn: () => {
      const grants = [...picked].map(parseGrant);
      return rolesApi.save(role?.id ?? null, name.trim(), grants);
    },
    invalidate: [['roles']],
    success: 'Role saved',
    errorFallback: 'Save failed',
    onSuccess: onSaved,
  });
  const toggle = (key: string) => {
    const next = new Set(picked);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    setPicked(next);
  };
  const everything = picked.has(WILDCARD);

  return (
    <div className="space-y-3">
      <Field>
        <FieldLabel htmlFor="role-name">Name</FieldLabel>
        <Input id="role-name" value={name} placeholder="Regional manager" onChange={(e) => setName(e.target.value)} />
      </Field>
      <FieldSet>
        <FieldLegend>Grants</FieldLegend>
        <Field orientation="horizontal">
          <Checkbox id="grant-wildcard" checked={everything} onCheckedChange={() => toggle(WILDCARD)} />
          <FieldLabel htmlFor="grant-wildcard" className="font-normal">
            Everything
            <span className="ml-1.5 font-mono text-xs text-muted-foreground" aria-hidden>*:*</span>
          </FieldLabel>
        </Field>
        {/* grouped the way an admin thinks about the org, with the key as the small print */}
        <div className="grid gap-x-6 gap-y-3 sm:grid-cols-2">
          {CAPABILITY_GROUPS.map((group) => {
            const keys = GRANTABLE.filter((c) => CAPABILITY_LABELS[c].group === group);
            if (keys.length === 0) return null;
            return (
              <div key={group} className="space-y-1.5">
                <p className="text-xs font-medium text-muted-foreground">{group}</p>
                {keys.map((c) => (
                  <Field key={c} orientation="horizontal">
                    <Checkbox
                      id={`grant-${c}`}
                      checked={everything || picked.has(c)}
                      disabled={everything}
                      onCheckedChange={() => toggle(c)}
                    />
                    <FieldLabel htmlFor={`grant-${c}`} className="font-normal">
                      {CAPABILITY_LABELS[c].label}
                      <span className="ml-1.5 font-mono text-xs text-muted-foreground" aria-hidden>
                        {c}
                      </span>
                    </FieldLabel>
                  </Field>
                ))}
              </div>
            );
          })}
        </div>
      </FieldSet>
      <Button className="w-full"
        disabled={!name.trim() || picked.size === 0 || save.isPending}
        onClick={() => save.mutate()}>
        {role ? 'Save changes' : 'Create role'}
      </Button>
    </div>
  );
}
