import { api } from '@locintel/api';
import { Button, cn, Sheet, SheetContent, SheetHeader, SheetTitle, SheetTrigger } from '@locintel/ui';
import { Link, useRouterState } from '@tanstack/react-router';
import { ChevronDown, LogOut, MapPin, MoreHorizontal, UserRound } from 'lucide-react';
import { useState } from 'react';
import { can } from '../../session';
import { useSessionTransition } from '../session-boundary';
import { TAB_BAR, type User, isActive, visibleGroups } from './nav';
import { ScopeTree } from './scope-tree';
import { OrgSwitcher } from './topbar';

export function TabBar({ me }: { me: User }) {
  const path = useRouterState({ select: (s) => s.location.pathname });
  const items = visibleGroups(me).flatMap((g) => g.items);
  const tabs = items.filter((n) => TAB_BAR.includes(n.to));
  const more = items.filter((n) => !TAB_BAR.includes(n.to));
  return (
    <nav
      aria-label="Areas"
      className="fixed inset-x-0 bottom-0 z-40 flex border-t bg-sidebar pb-[env(safe-area-inset-bottom)] md:hidden"
    >
      {tabs.map((n) => {
        const Icon = n.icon;
        const active = isActive(path, n.to);
        return (
          <Link
            key={n.to}
            to={n.to}
            aria-current={active ? 'page' : undefined}
            className={cn(
              'flex flex-1 flex-col items-center gap-1 py-2 text-[10px] font-medium text-muted-foreground',
              active && 'text-foreground',
            )}
          >
            <span
              className={cn(
                'flex h-7 w-11 items-center justify-center rounded-full',
                active && 'bg-accent text-primary',
              )}
            >
              <Icon className="size-[18px]" />
            </span>
            {n.label}
          </Link>
        );
      })}
      <Sheet>
        <SheetTrigger
          render={
            <button
              type="button"
              className="flex flex-1 flex-col items-center gap-1 py-2 text-[10px] font-medium text-muted-foreground"
            />
          }
        >
          <span className="flex h-7 w-11 items-center justify-center rounded-full">
            <MoreHorizontal className="size-[18px]" />
          </span>
          More
        </SheetTrigger>
        <SheetContent side="bottom" className="max-h-[85vh] overflow-auto rounded-t-2xl pb-8">
          <SheetHeader className="pb-0">
            <SheetTitle>{me.organizations.find((o) => o.id === me.activeOrg)?.name}</SheetTitle>
          </SheetHeader>
          <div className="px-4">
            <OrgSwitcher me={me} />
          </div>
          <nav aria-label="More areas" className="flex flex-col">
            {more.map((n) => {
              const Icon = n.icon;
              return (
                <Link
                  key={n.to}
                  to={n.to}
                  className="flex h-12 items-center gap-3 border-b px-4 text-[15px] last:border-b-0"
                >
                  <Icon className="size-[18px] text-muted-foreground" />
                  <span className="flex-1">{n.label}</span>
                </Link>
              );
            })}
          </nav>
          <div className="flex items-center gap-3 px-4 pt-2">
            <span className="flex size-9 items-center justify-center rounded-full border bg-card text-xs font-semibold">
              {initials(me)}
            </span>
            <span className="min-w-0 flex-1 leading-tight">
              <Link to="/account" className="block truncate text-sm font-medium hover:underline">
                {me.name ?? me.email}
              </Link>
              <span className="block truncate text-xs text-muted-foreground">{me.email}</span>
            </span>
            <SignOutButton />
          </div>
        </SheetContent>
      </Sheet>
    </nav>
  );
}

export function ScopeSheet({ me, scopeName }: { me: User; scopeName: string }) {
  // controlled so a pick closes the sheet: on a phone the choice IS the action
  const [open, setOpen] = useState(false);
  if (!can(me, 'sites:read')) return null;
  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger
        render={
          <Button variant="outline" size="sm" className="min-w-0 flex-1 justify-start gap-2" />
        }
      >
        <MapPin className="size-4 text-muted-foreground" />
        <span className="text-muted-foreground">Scope</span>
        <span className="truncate font-semibold">{scopeName}</span>
        <ChevronDown className="ml-auto size-4 text-muted-foreground" />
      </SheetTrigger>
      <SheetContent side="bottom" className="max-h-[85vh] overflow-auto rounded-t-2xl pb-8">
        <SheetHeader>
          <SheetTitle>Scope</SheetTitle>
        </SheetHeader>
        <div className="px-2">
          <ScopeTree
            me={me}
            rowClass="h-11 text-[15px]"
            rowHeight={46}
            className="max-h-[65vh]"
            onPick={() => setOpen(false)}
          />
        </div>
      </SheetContent>
    </Sheet>
  );
}

export function SignOutButton() {
  const changeSession = useSessionTransition();
  return (
    <Button
      variant="ghost"
      size="sm"
      onClick={async () => {
        await changeSession(() => api.post('/auth/logout'));
      }}
    >
      <LogOut className="size-4" aria-hidden />
      Sign out
    </Button>
  );
}

export const initials = (me: User) =>
  (me.name ?? me.email)
    .split(/[\s@._-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part.charAt(0).toUpperCase())
    .join('') || <UserRound className="size-4" />;
