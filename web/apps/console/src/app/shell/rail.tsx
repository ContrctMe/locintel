import { cn, Sidebar, SidebarContent, SidebarGroup, SidebarGroupContent, SidebarHeader, SidebarMenu, SidebarMenuButton, SidebarMenuItem, Tooltip, TooltipContent, TooltipTrigger } from '@locintel/ui';
import { Link, useRouterState } from '@tanstack/react-router';
import { type NavItem, type User, isActive, visibleGroups } from './nav';
import { ScopeTree } from './scope-tree';

/**
 * Direction B on the shadcn Sidebar's double-sidebar pattern: one
 * icon-collapsible sidebar holding the 76px area rail and, on the pages
 * that read it, the 240px Scope panel. Collapsed, only the rail stays; the
 * trigger in the top bar and Cmd/Ctrl-B toggle it. Nothing here is drawn
 * by hand - rows are SidebarMenuButtons.
 */
export function AppSidebar({ me, scoped }: { me: User; scoped: boolean }) {
  const path = useRouterState({ select: (s) => s.location.pathname });
  const groups = visibleGroups(me);
  return (
    <Sidebar
      collapsible="icon"
      className="hidden overflow-hidden md:flex *:data-[sidebar=sidebar]:flex-row"
    >
      {/* the area rail: always icons, a tooltip names each */}
      <Sidebar collapsible="none" className="w-[calc(var(--sidebar-width-icon)+1px)]! border-r">
        <SidebarHeader className="items-center pt-3">
          <Link
            to="/"
            aria-label="LocIntel home"
            className="flex size-8 items-center justify-center rounded-lg bg-foreground text-sm font-extrabold text-background"
          >
            P
          </Link>
        </SidebarHeader>
        <SidebarContent>
          <nav aria-label="Areas" className="contents">
            {groups.map((group, i) => (
              <SidebarGroup key={group.label ?? 'operate'} className={cn('items-center px-0', i > 0 && 'border-t')}>
                <SidebarGroupContent>
                  <SidebarMenu className="items-center gap-1">
                    {group.items.map((n) => (
                      <RailLink key={n.to} item={n} active={isActive(path, n.to)} />
                    ))}
                  </SidebarMenu>
                </SidebarGroupContent>
              </SidebarGroup>
            ))}
          </nav>
        </SidebarContent>
      </Sidebar>
      {/* the Scope panel: what the site-reading pages ask under */}
      {scoped && (
        <Sidebar
          collapsible="none"
          role="complementary"
          aria-label="Scope"
          className="hidden min-w-0 flex-1 md:flex"
        >
          <SidebarContent className="p-2">
            <ScopeTree me={me} />
          </SidebarContent>
        </Sidebar>
      )}
    </Sidebar>
  );
}

export function RailLink({ item, active }: { item: NavItem; active: boolean }) {
  const Icon = item.icon;
  return (
    <SidebarMenuItem>
      <Tooltip>
        <TooltipTrigger
          render={
            <SidebarMenuButton
              isActive={active}
              size="lg"
              className="size-11 justify-center rounded-[10px] border border-transparent p-0 data-[active=true]:border-sidebar-border"
              render={<Link to={item.to} aria-current={active ? 'page' : undefined} />}
            />
          }
        >
          <Icon className="size-[18px]" aria-hidden />
          <span className="sr-only">{item.label}</span>
        </TooltipTrigger>
        <TooltipContent side="right">{item.label}</TooltipContent>
      </Tooltip>
    </SidebarMenuItem>
  );
}
