import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of } from 'rxjs';
import { AuthService } from '../core/auth.service';
import { Api } from '../core/api.service';
import { Perm } from '../core/api.models';
import { I18n, TranslatePipe } from '../core/i18n.service';
import { Icon } from '../shared/icon';

interface NavItem { path: string; label: string; icon: string; perms: string[]; }

/** App frame from the mockup: full-width top bar (logo, search, notifications, language, user) over a sidebar with a solid active item. */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, Icon],
  template: `
    <div class="app" [class.nav-open]="navOpen()">
      <header class="topbar">
        <div class="brandbar">
          <button class="icon-btn menu-btn" type="button" [attr.aria-label]="'nav.menu' | t" (click)="navOpen.set(!navOpen())"><app-icon name="menu" /></button>
          <span class="logo-cube"><app-icon name="cube" [size]="28" /></span>
          <strong>Licensing Platform</strong>
        </div>

        <form class="topsearch" role="search" (submit)="$event.preventDefault(); search(q.value)">
          <app-icon name="search" [size]="16" />
          <input #q type="search" [placeholder]="'nav.searchAll' | t" [attr.aria-label]="'nav.searchAll' | t" />
        </form>

        <div class="top-actions">
          @if (auth.isPlatformAdmin()) {
            <select class="scope-select" [value]="auth.tenantScope() ?? ''" (change)="setScope($any($event.target).value)" [attr.aria-label]="'nav.scope' | t">
              <option value="">{{ 'nav.allTenants' | t }}</option>
              @for (t of tenants(); track t.id) { <option [value]="t.id">{{ t.name }}</option> }
            </select>
          }
          <a class="icon-btn" routerLink="/notifications" [attr.aria-label]="'nav.notifications' | t">
            <app-icon name="bell" [size]="20" />
            @if (alertCount() > 0) { <span class="dot-count">{{ alertCount() }}</span> }
          </a>
          <button class="lang-btn" type="button" (click)="i18n.toggle()"><app-icon name="globe" [size]="18" />{{ 'nav.language' | t }}<app-icon name="chevronDown" [size]="14" /></button>
          <a class="icon-btn" routerLink="/settings" [attr.aria-label]="'nav.settings' | t"><app-icon name="settings" [size]="20" /></a>
          <button class="user-chip" type="button" (click)="menuOpen.set(!menuOpen()); $event.stopPropagation()" aria-haspopup="menu" [attr.aria-expanded]="menuOpen()">
            @if (auth.user()?.imageUrl) { <img class="avatar" [src]="auth.user()!.imageUrl" alt="" style="object-fit:cover;padding:0" /> }
            @else { <span class="avatar">{{ initial() }}</span> }
            <span class="who"><strong>{{ auth.user()?.fullName }}</strong><small>{{ 'role.' + auth.user()?.role | t }}</small></span>
            <app-icon name="chevronDown" [size]="16" />
            @if (menuOpen()) {
              <div class="menu" role="menu">
                <div style="padding:8px 10px"><strong>{{ auth.user()?.fullName }}</strong><br /><small class="muted" dir="ltr">{{ auth.user()?.email }}</small></div>
                <a role="menuitem" routerLink="/settings"><app-icon name="user" [size]="16" />{{ 'nav.profile' | t }}</a>
                <button role="menuitem" type="button" class="danger" (click)="auth.logout()"><app-icon name="logout" [size]="16" />{{ 'nav.logout' | t }}</button>
              </div>
            }
          </button>
        </div>
      </header>

      <aside class="sidebar">
        <nav aria-label="main">
          @for (item of nav(); track item.path) {
            <a [routerLink]="item.path" routerLinkActive="active" (click)="navOpen.set(false)"><app-icon [name]="item.icon" />{{ item.label | t }}</a>
          }
        </nav>
      </aside>
      <div class="backdrop" (click)="navOpen.set(false)"></div>

      <main class="content">
        <!-- Changing the tenant scope re-creates the page so every screen reloads with the new scope. -->
        @for (key of [scopeKey()]; track key) {
          <router-outlet />
        }
      </main>
    </div>
  `,
})
export class Shell {
  readonly auth = inject(AuthService);
  readonly i18n = inject(I18n);
  private api = inject(Api);
  private router = inject(Router);
  readonly navOpen = signal(false);
  readonly menuOpen = signal(false);

  private readonly items: NavItem[] = [
    { path: '/dashboard', label: 'nav.dashboard', icon: 'dashboard', perms: [Perm.ReportsRead] },
    { path: '/tenants', label: 'nav.tenants', icon: 'building', perms: [Perm.TenantsRead] },
    { path: '/customers', label: 'nav.customers', icon: 'users', perms: [Perm.CustomersRead] },
    { path: '/products', label: 'nav.products', icon: 'package', perms: [Perm.CatalogRead] },
    { path: '/plans', label: 'nav.plans', icon: 'layers', perms: [Perm.CatalogRead] },
    { path: '/subscriptions', label: 'nav.subscriptions', icon: 'repeat', perms: [Perm.SubscriptionsRead] },
    { path: '/licenses', label: 'nav.licenses', icon: 'key', perms: [Perm.LicensesRead] },
    { path: '/activations', label: 'nav.activations', icon: 'monitor', perms: [Perm.LicensesRead] },
    { path: '/integrations', label: 'nav.apiClients', icon: 'code', perms: [Perm.ApiClientsManage, Perm.SigningKeysManage] },
    { path: '/reports', label: 'nav.reports', icon: 'chart', perms: [Perm.ReportsRead] },
    { path: '/audit', label: 'nav.audit', icon: 'scroll', perms: [Perm.AuditRead] },
    { path: '/notifications', label: 'nav.notifications', icon: 'bell', perms: [Perm.ReportsRead] },
    { path: '/users', label: 'nav.usersRoles', icon: 'userCog', perms: [Perm.UsersManage] },
    { path: '/settings', label: 'nav.settings', icon: 'settings', perms: [] },
  ];

  readonly nav = computed(() => {
    this.auth.user();
    return this.items.filter(i => i.perms.length === 0 || this.auth.canAny(...i.perms));
  });

  readonly initial = computed(() => (this.auth.user()?.fullName ?? '?').trim().charAt(0));

  readonly tenants = toSignal(
    this.auth.isPlatformAdmin()
      ? this.api.tenants({ pageSize: 200 }).pipe(map(p => p.items), catchError(() => of([])))
      : of([]),
    { initialValue: [] },
  );

  /** Bell badge = number of "needs attention" items. */
  readonly alertCount = toSignal(
    this.auth.can(Perm.ReportsRead)
      ? this.api.overview().pipe(
          map(o => [o.attention.expiredLicenses, o.attention.renewalsDue, o.attention.limitReached, o.attention.suspiciousActivations].filter(n => n > 0).length),
          catchError(() => of(0)))
      : of(0),
    { initialValue: 0 },
  );

  readonly scopeKey = computed(() => this.auth.tenantScope() ?? 'all');

  @HostListener('document:click') closeMenu() { this.menuOpen.set(false); }

  setScope(id: string) { this.auth.setTenantScope(id || null); }

  /** Global search: license numbers/keys go to licenses, everything else to customers (or licenses for customer users). */
  search(term: string) {
    const q = term.trim();
    if (!q) return;
    const target = /^(lic-|[a-z0-9]{6}-)/i.test(q) || !this.auth.can(Perm.CustomersRead) ? '/licenses' : '/customers';
    this.router.navigate([target], { queryParams: { search: q } });
  }
}
