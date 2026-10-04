import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of } from 'rxjs';
import { AuthService } from '../core/auth.service';
import { Api } from '../core/api.service';
import { Perm } from '../core/api.models';
import { I18n, TranslatePipe } from '../core/i18n.service';
import { Icon } from '../shared/icon';

interface NavItem { path: string; label: string; icon: string; perms: string[]; }
interface NavGroup { label: string; items: NavItem[]; }

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, Icon],
  template: `
    <div class="shell" [class.nav-open]="navOpen()">
      <aside class="sidebar">
        <div class="brand">
          <span class="brand-mark"><app-icon name="shield" [size]="22" /></span>
          <div>
            <strong>{{ 'app.name' | t }}</strong>
            <small>{{ subtitle() }}</small>
          </div>
        </div>
        @for (group of nav(); track group.label) {
          <div class="nav-group">{{ group.label | t }}</div>
          <nav [attr.aria-label]="group.label | t">
            @for (item of group.items; track item.path) {
              <a [routerLink]="item.path" routerLinkActive="active" (click)="navOpen.set(false)">
                <app-icon [name]="item.icon" />{{ item.label | t }}
              </a>
            }
          </nav>
        }
      </aside>
      <div class="backdrop" (click)="navOpen.set(false)"></div>

      <div class="main">
        <header class="topbar">
          <button class="btn btn-ghost icon-only menu-btn" type="button" [attr.aria-label]="'nav.menu' | t" (click)="navOpen.set(!navOpen())">
            <app-icon name="menu" />
          </button>

          @if (auth.isPlatformAdmin()) {
            <label class="scope">
              <app-icon name="building" />
              <span>{{ 'nav.scope' | t }}</span>
              <select [value]="auth.tenantScope() ?? ''" (change)="setScope($any($event.target).value)">
                <option value="">{{ 'nav.allTenants' | t }}</option>
                @for (t of tenants(); track t.id) {
                  <option [value]="t.id">{{ t.name }}</option>
                }
              </select>
            </label>
          }

          <span class="spacer"></span>
          <button class="btn btn-ghost" type="button" (click)="i18n.toggle()"><app-icon name="globe" />{{ 'nav.language' | t }}</button>
          <span class="avatar" aria-hidden="true">{{ initial() }}</span>
          <div class="who">
            <strong>{{ auth.user()?.fullName }}</strong>
            <small>{{ 'role.' + auth.user()?.role | t }}</small>
          </div>
          <button class="btn btn-ghost icon-only" type="button" [attr.aria-label]="'nav.logout' | t" [title]="'nav.logout' | t" (click)="auth.logout()">
            <app-icon name="logout" />
          </button>
        </header>

        <main class="content">
          <!-- Changing the tenant scope re-creates the page so every screen reloads with the new scope. -->
          @for (key of [scopeKey()]; track key) {
            <router-outlet />
          }
        </main>
      </div>
    </div>
  `,
})
export class Shell {
  readonly auth = inject(AuthService);
  readonly i18n = inject(I18n);
  private api = inject(Api);
  readonly navOpen = signal(false);

  private readonly groups: NavGroup[] = [
    { label: 'nav.group.main', items: [
      { path: '/dashboard', label: 'nav.dashboard', icon: 'dashboard', perms: [Perm.ReportsRead] },
      { path: '/tenants', label: 'nav.tenants', icon: 'building', perms: [Perm.TenantsRead] },
      { path: '/customers', label: 'nav.customers', icon: 'users', perms: [Perm.CustomersRead] },
    ] },
    { label: 'nav.group.catalog', items: [
      { path: '/products', label: 'nav.products', icon: 'package', perms: [Perm.CatalogRead] },
      { path: '/subscriptions', label: 'nav.subscriptions', icon: 'repeat', perms: [Perm.SubscriptionsRead] },
      { path: '/licenses', label: 'nav.licenses', icon: 'key', perms: [Perm.LicensesRead] },
    ] },
    { label: 'nav.group.ops', items: [
      { path: '/integrations', label: 'nav.apiClients', icon: 'plug', perms: [Perm.ApiClientsManage, Perm.SigningKeysManage] },
      { path: '/reports', label: 'nav.reports', icon: 'chart', perms: [Perm.ReportsRead] },
      { path: '/audit', label: 'nav.audit', icon: 'scroll', perms: [Perm.AuditRead] },
    ] },
    { label: 'nav.group.admin', items: [
      { path: '/users', label: 'nav.users', icon: 'userCog', perms: [Perm.UsersManage] },
      { path: '/settings', label: 'nav.settings', icon: 'settings', perms: [] },
    ] },
  ];

  readonly nav = computed(() => {
    this.auth.user();
    return this.groups
      .map(g => ({ ...g, items: g.items.filter(i => i.perms.length === 0 || this.auth.canAny(...i.perms)) }))
      .filter(g => g.items.length > 0);
  });

  readonly subtitle = computed(() => {
    const u = this.auth.user();
    return u?.customerName ?? u?.tenantName ?? this.i18n.t('nav.allTenants');
  });

  readonly initial = computed(() => (this.auth.user()?.fullName ?? '?').trim().charAt(0));

  readonly tenants = toSignal(
    this.auth.isPlatformAdmin()
      ? this.api.tenants({ pageSize: 200 }).pipe(map(p => p.items), catchError(() => of([])))
      : of([]),
    { initialValue: [] },
  );

  readonly scopeKey = computed(() => this.auth.tenantScope() ?? 'all');

  setScope(id: string) { this.auth.setTenantScope(id || null); }
}
