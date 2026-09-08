import type { components } from '@locintel/api';
import { Button, Input, Label } from '@locintel/ui';
import { useState } from 'react';
import { useApiMutation } from '../../../lib/mutation';
import { rolesApi } from '../api';
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

  return (
    <div className="space-y-3">
      <div className="space-y-1">
        <Label htmlFor="role-name">Name</Label>
        <Input id="role-name" value={name} onChange={(e) => setName(e.target.value)} />
      </div>
      <div className="space-y-1">
        <Label>Grants</Label>
        <div className="grid grid-cols-2 gap-1.5">
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={picked.has(WILDCARD)}
              onChange={() => toggle(WILDCARD)} />
            <span className="font-mono">*:* (everything)</span>
          </label>
          {GRANTABLE.map((c) => (
            <label key={c} className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={picked.has(c)} onChange={() => toggle(c)} />
              <span className="font-mono">{c}</span>
            </label>
          ))}
        </div>
      </div>
      <Button className="w-full"
        disabled={!name.trim() || picked.size === 0 || save.isPending}
        onClick={() => save.mutate()}>
        {role ? 'Save changes' : 'Create role'}
      </Button>
    </div>
  );
}
