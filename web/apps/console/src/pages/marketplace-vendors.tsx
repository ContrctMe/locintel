import { api } from '@locintel/api';
import { Button, Card, CardContent, Input, Select } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useApiMutation } from '../lib/mutation';
import type { Page } from '../lib/paging';
import { can, useMe } from '../session';
import { CATEGORIES, categoryLabel, type VendorSummary } from './marketplace';

/** The catalog: every published vendor org, with credential health and your org's preferred/blocked marks. */
export function MarketplaceVendorsPage() {
  const { data: me } = useMe();
  const manage = can(me, 'marketplace:manage');
  const [category, setCategory] = useState('');
  const [area, setArea] = useState('');
  const [q, setQ] = useState('');
  const params = new URLSearchParams({ limit: '100' });
  if (category) params.set('category', category);
  if (area.trim()) params.set('area', area.trim());
  if (q.trim()) params.set('q', q.trim());
  const { data } = useQuery({
    queryKey: ['marketplace', 'vendors', params.toString()],
    queryFn: () => api.get<Page<VendorSummary>>(`/api/marketplace/vendors?${params}`),
  });
  const mark = useApiMutation({
    mutationFn: (input: { vendorOrgId: string; blocked: boolean }) => api.post('/api/marketplace/preferred', input),
    invalidate: [['marketplace']],
    success: 'Saved',
  });
  const unmark = useApiMutation({
    mutationFn: async (vendorOrgId: string) => {
      const rows = await api.get<{ id: string; vendorOrgId: string }[]>('/api/marketplace/preferred');
      const row = rows.find((r) => r.vendorOrgId === vendorOrgId);
      if (row) await api.del(`/api/marketplace/preferred/${row.id}`);
    },
    invalidate: [['marketplace']],
    success: 'Removed',
  });

  return (
    <div className="max-w-5xl space-y-6">
      <div>
        <Link to="/marketplace" className="text-sm text-muted-foreground hover:underline">← Marketplace</Link>
        <h1 className="text-2xl font-semibold">Vendors</h1>
      </div>
      <div className="flex flex-wrap gap-2">
        <Input className="w-56" placeholder="Search" value={q} onChange={(e) => setQ(e.target.value)} />
        <Select className="w-48" value={category} onChange={(e) => setCategory(e.target.value)}>
          <option value="">Any category</option>
          {CATEGORIES.map((c) => <option key={c} value={c}>{categoryLabel(c)}</option>)}
        </Select>
        <Input className="w-28" placeholder="Area (CA)" value={area} onChange={(e) => setArea(e.target.value)} />
      </div>
      <div className="grid gap-4 md:grid-cols-2">
        {data?.items.length === 0 && <p className="text-sm text-muted-foreground">No published vendors match.</p>}
        {data?.items.map((v) => (
          <Card key={v.orgId}>
            <CardContent className="space-y-2 pt-4">
              <div className="flex items-center justify-between">
                <span className="font-medium">{v.preferred ? '★ ' : ''}{v.name}</span>
                {v.blocked && <span className="text-xs font-medium uppercase text-destructive">Blocked</span>}
              </div>
              <p className="text-sm text-muted-foreground">{v.description || '—'}</p>
              <p className="text-xs text-muted-foreground">
                {v.categories.map(categoryLabel).join(', ')}
                {v.serviceAreas.length > 0 && ` · ${v.serviceAreas.join(', ')}`}
              </p>
              <p className="text-xs">
                <span className="text-emerald-700 dark:text-emerald-300">{v.validCredentials} valid credentials</span>
                {v.expiredCredentials > 0 && <span className="ml-2 text-destructive">{v.expiredCredentials} expired</span>}
              </p>
              {manage && (
                <div className="flex gap-2">
                  {v.preferred || v.blocked ? (
                    <Button size="sm" variant="outline" onClick={() => unmark.mutate(v.orgId)}>Clear mark</Button>
                  ) : (
                    <>
                      <Button size="sm" variant="outline" onClick={() => mark.mutate({ vendorOrgId: v.orgId, blocked: false })}>Prefer</Button>
                      <Button size="sm" variant="ghost" onClick={() => mark.mutate({ vendorOrgId: v.orgId, blocked: true })}>Block</Button>
                    </>
                  )}
                </div>
              )}
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
}
