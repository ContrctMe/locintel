import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button, ConfirmButton, Field, FieldLabel, Input, Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemTitle, Select, Switch } from '@locintel/ui';
import { Panel } from '../../components/page';
import { useApiMutation } from '../../lib/mutation';
import { settingsApi } from './api';

export function SiteAttributesCard() {
  const [key, setKey] = useState('');
  const [label, setLabel] = useState('');
  const [type, setType] = useState('Text');
  const [isPublic, setIsPublic] = useState(false);

  const { data: definitions } = useQuery({
    queryKey: ['site-attributes'],
    queryFn: ({ signal }) => settingsApi.attributes(signal),
  });
  const create = useApiMutation({
    mutationFn: () =>
      settingsApi.createAttribute({ key: key.trim(), label: label.trim(), type, public: isPublic }),
    invalidate: [['site-attributes']],
    success: 'Attribute added',
    onSuccess: () => {
      setKey('');
      setLabel('');
      setIsPublic(false);
    },
  });
  const remove = useApiMutation({
    mutationFn: (id: string) => settingsApi.removeAttribute(id),
    invalidate: [['site-attributes'], ['site']],
    success: 'Attribute removed - its values are gone from every site',
  });

  return (
    <Panel title="Site attributes" bodyClassName="space-y-3">
        <p className="text-sm text-muted-foreground">
          Your own fields on every site - a drive-thru flag, a cost center, a manager name.
          Public attributes appear on the site&apos;s public page; the rest stay internal.
        </p>
        {definitions && definitions.length > 0 && (
          <ItemGroup>
            {definitions.map((d) => (
              <Item key={d.id} variant="outline" size="sm">
                <ItemContent>
                  <ItemTitle>{d.label}</ItemTitle>
                  <ItemDescription>
                    {d.key} · {d.type}
                    {d.public && ' · public'}
                  </ItemDescription>
                </ItemContent>
                <ItemActions>
                  <ConfirmButton size="sm" variant="ghost" confirmLabel="Delete? Values go too"
                    disabled={remove.isPending} onConfirm={() => remove.mutate(d.id)}>
                    Delete
                  </ConfirmButton>
                </ItemActions>
              </Item>
            ))}
          </ItemGroup>
        )}
        <div className="flex flex-wrap items-end gap-2">
          <Field>
            <FieldLabel htmlFor="attr-label">Label</FieldLabel>
            <Input id="attr-label" className="w-40" value={label} placeholder="Drive-thru"
              onChange={(e) => {
                setLabel(e.target.value);
                setKey(e.target.value.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, ''));
              }} />
          </Field>
          <Field>
            <FieldLabel htmlFor="attr-key">Key</FieldLabel>
            <Input id="attr-key" className="w-36 font-mono text-xs" value={key}
              onChange={(e) => setKey(e.target.value)} />
          </Field>
          <Field>
            <FieldLabel htmlFor="attr-type">Type</FieldLabel>
            <Select id="attr-type" className="w-28" value={type}
              onChange={(e) => setType(e.target.value)}>
              <option>Text</option>
              <option>Number</option>
              <option>Boolean</option>
            </Select>
          </Field>
          <label className="flex h-9 items-center gap-2 text-sm">
            <Switch checked={isPublic} onCheckedChange={(checked) => setIsPublic(checked)} />
            Public
          </label>
          <Button size="sm" disabled={!key.trim() || !label.trim() || create.isPending}
            onClick={() => create.mutate()}>
            Add
          </Button>
        </div>
      </Panel>
  );
}
