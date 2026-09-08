import {
  Avatar,
  AvatarFallback,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@locintel/ui';
import { useNavigate } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { LogOut, MoonStar, Settings, Sun, UserRound } from 'lucide-react';
import { api } from '@locintel/api';
import { useSessionTransition } from '../app/session-boundary';
import { toggleTheme, useTheme } from '../app/theme';

/**
 * The header's account menu (the app-shell block's user menu, on our
 * account): who you are, where your account and settings live, the theme,
 * and sign out - through the session boundary, so in-flight writes finish
 * on the old cookie first.
 */
export function UserMenu({
  email,
  name,
  canManageOrg,
  footer,
}: {
  email: string;
  name?: string | null;
  canManageOrg: boolean;
  /** A muted last line: the build the console is talking to. */
  footer?: ReactNode;
}) {
  const navigate = useNavigate();
  const theme = useTheme();
  const changeSession = useSessionTransition();
  const initials = (name ?? email)
    .split(/[\s@._-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('');
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <button
            type="button"
            aria-label="Account menu"
            className="flex size-8 items-center justify-center rounded-full outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
          />
        }
      >
        <Avatar className="size-8 border">
          <AvatarFallback className="text-xs font-semibold">{initials}</AvatarFallback>
        </Avatar>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-60">
        {/* Base UI: a group label lives inside a group */}
        <DropdownMenuGroup>
          <DropdownMenuLabel className="flex flex-col">
            <span className="truncate font-medium">{name ?? email}</span>
            {name && <span className="truncate text-xs font-normal text-muted-foreground">{email}</span>}
          </DropdownMenuLabel>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          <DropdownMenuItem onClick={() => void navigate({ to: '/account' })}>
            <UserRound aria-hidden />
            Account
          </DropdownMenuItem>
          {canManageOrg && (
            <DropdownMenuItem onClick={() => void navigate({ to: '/settings' })}>
              <Settings aria-hidden />
              Organization settings
            </DropdownMenuItem>
          )}
          <DropdownMenuItem onClick={() => toggleTheme()}>
            {theme === 'dark' ? <Sun aria-hidden /> : <MoonStar aria-hidden />}
            {theme === 'dark' ? 'Light theme' : 'Dark theme'}
          </DropdownMenuItem>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          onClick={() => {
            void changeSession(() => api.post('/auth/logout'));
          }}
        >
          <LogOut aria-hidden />
          Sign out
        </DropdownMenuItem>
        {footer && <div className="px-2 pb-1 pt-1.5 text-[11px] text-muted-foreground">{footer}</div>}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
