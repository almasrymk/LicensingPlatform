import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Customer, License, Overview, Paged, Product, Subscription, Tenant, User } from '../core/api.models';
import { I18n, LocalDatePipe, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Donut } from '../shared/charts';
import { Icon } from '../shared/icon';
import { StateView, StatusBadge, loader } from '../shared/ui';

type Tab = 'overview' | 'users' | 'customers' | 'subscriptions' | 'licenses';

interface TenantData {
  tenant: Tenant; overview: Overview; products: Paged<Product>; users: Paged<User>; customers: Paged<Customer>;
  subscriptions: Paged<Subscription>; licenses: Paged<License>;
}

/** Tenant details from the mockup: header with status, tabs, KPI tiles, products used, subscription status donut. */
@Component({
  selector: 'app-tenant-details',
  imports: [RouterLink, TranslatePipe, LocalNumberPipe, LocalDatePipe, StateView, StatusBadge, Icon, Donut],
  template: `
    <app-state [loading]="data.loading() && !data.data()" [error]="data.error()" (retry)="data.load()">
      @if (data.data(); as d) {
        <nav class="crumbs"><a routerLink="/tenants">{{ 'nav.tenants' | t }}</a><span>/</span><span>{{ d.tenant.name }}</span></nav>
        <header class="page-head">
          <div class="entity-head">
            <span class="tile blue"><app-icon name="building" [size]="24" /></span>
            <div>
              <h1>{{ d.tenant.name }} <app-status [value]="d.tenant.status" /></h1>
              <p><span class="mono" dir="ltr">{{ d.tenant.code }}</span> · {{ 'td.since' | t: { date: (d.tenant.createdAt | date2) } }}</p>
            </div>
          </div>
          <div class="actions">
            <button class="btn" type="button" (click)="auth.setTenantScope(d.tenant.id)"><app-icon name="eye" [size]="16" />{{ 'td.viewData' | t }}</button>
            @if (auth.can(auth.perm.TenantsManage)) {
              @if (d.tenant.status === 'Active') {
                <button class="btn btn-warn" type="button" (click)="suspend(d.tenant)"><app-icon name="ban" [size]="16" />{{ 'tenants.suspend' | t }}</button>
              } @else {
                <button class="btn btn-primary" type="button" (click)="resume(d.tenant)"><app-icon name="checkCircle" [size]="16" />{{ 'customers.activate' | t }}</button>
              }
            }
          </div>
        </header>

        <div class="tabs" role="tablist">
          @for (t of tabs; track t.id) {
            <button type="button" role="tab" [class.active]="tab() === t.id" [attr.aria-selected]="tab() === t.id" (click)="tab.set(t.id)">{{ t.label | t }}</button>
          }
        </div>

        @switch (tab()) {
          @case ('overview') {
            <section class="mini-kpis">
              <div class="mini"><span class="tile sm blue"><app-icon name="users" /></span><div><small>{{ 'col.users' | t }}</small><strong>{{ d.tenant.users | num }}</strong></div></div>
              <div class="mini"><span class="tile sm green"><app-icon name="list" /></span><div><small>{{ 'td.activeSubs' | t }}</small><strong>{{ d.overview.subscriptions.value | num }}</strong></div></div>
              <div class="mini"><span class="tile sm violet"><app-icon name="key" /></span><div><small>{{ 'td.activeLicenses' | t }}</small><strong>{{ d.overview.activeLicenses.value | num }}</strong></div></div>
              <div class="mini"><span class="tile sm orange"><app-icon name="monitor" /></span><div><small>{{ 'td.activeDevices' | t }}</small><strong>{{ devices() | num }}</strong></div></div>
            </section>
            <section class="lists">
              <div class="card">
                <h2>{{ 'td.productsUsed' | t }}</h2>
                @for (p of d.products.items; track p.id; let i = $index) {
                  <div class="usage-row">
                    <span class="tile sm" [class]="'tile sm ' + tone(i)"><app-icon name="cube" /></span>
                    <div><strong>{{ p.name }}</strong><small>{{ 'products.plansN' | t: { n: p.plans } }}</small></div>
                    <small dir="ltr">{{ p.licenses }} / {{ maxLicenses() }} {{ 'col.licenses' | t }}</small>
                    <div class="bar"><span [class]="['', 'green', 'violet'][i % 3]" [style.width.%]="(p.licenses / maxLicenses()) * 100"></span></div>
                  </div>
                } @empty { <p class="muted">{{ 'common.empty' | t }}</p> }
              </div>
              <div class="card">
                <h2>{{ 'td.subStatus' | t }}</h2>
                <div class="donut-wrap">
                  <app-donut [slices]="subSlices()" [centerValue]="d.subscriptions.total" [centerLabel]="'td.total' | t" [size]="150" />
                  <ul class="legend-list">
                    @for (s of subSlices(); track s.label) {
                      <li><i [style.background]="s.color"></i><span>{{ 'status.' + s.label | t }}</span><b>{{ s.value }}</b><span></span></li>
                    }
                  </ul>
                </div>
              </div>
            </section>
          }
          @case ('users') {
            <div class="table-card"><div class="table-wrap"><table>
              <thead><tr><th>{{ 'col.name' | t }}</th><th>{{ 'common.email' | t }}</th><th>{{ 'users.role' | t }}</th><th>{{ 'col.status' | t }}</th><th>{{ 'users.lastLogin' | t }}</th></tr></thead>
              <tbody>
                @for (u of d.users.items; track u.id) {
                  <tr><td><strong>{{ u.fullName }}</strong></td><td dir="ltr">{{ u.email }}</td><td>{{ 'role.' + u.role | t }}</td>
                    <td><app-status [value]="u.isActive ? 'Active' : 'Disabled'" /></td><td>{{ u.lastLoginAt | date2: true }}</td></tr>
                } @empty { <tr><td colspan="5" class="muted">{{ 'common.empty' | t }}</td></tr> }
              </tbody>
            </table></div></div>
          }
          @case ('customers') {
            <div class="table-card"><div class="table-wrap"><table>
              <thead><tr><th>{{ 'col.name' | t }}</th><th>{{ 'common.email' | t }}</th><th>{{ 'common.country' | t }}</th><th>{{ 'col.licenses' | t }}</th><th>{{ 'col.status' | t }}</th></tr></thead>
              <tbody>
                @for (c of d.customers.items; track c.id) {
                  <tr><td><strong>{{ c.name }}</strong></td><td dir="ltr">{{ c.email ?? '—' }}</td><td>{{ c.country ?? '—' }}</td><td>{{ c.activeLicenses }}</td><td><app-status [value]="c.status" /></td></tr>
                } @empty { <tr><td colspan="5" class="muted">{{ 'common.empty' | t }}</td></tr> }
              </tbody>
            </table></div></div>
          }
          @case ('subscriptions') {
            <div class="table-card"><div class="table-wrap"><table>
              <thead><tr><th>{{ 'col.customer' | t }}</th><th>{{ 'col.product' | t }}</th><th>{{ 'col.plan' | t }}</th><th>{{ 'col.start' | t }}</th><th>{{ 'col.end' | t }}</th><th>{{ 'col.status' | t }}</th></tr></thead>
              <tbody>
                @for (s of d.subscriptions.items; track s.id) {
                  <tr><td><strong>{{ s.customerName }}</strong></td><td>{{ s.productName }}</td><td>{{ s.planName }}</td><td>{{ s.startDate | date2 }}</td>
                    <td>{{ s.isLifetime ? ('common.lifetime' | t) : (s.endDate | date2) }}</td><td><app-status [value]="s.status" /></td></tr>
                } @empty { <tr><td colspan="6" class="muted">{{ 'common.empty' | t }}</td></tr> }
              </tbody>
            </table></div></div>
          }
          @case ('licenses') {
            <div class="table-card"><div class="table-wrap"><table>
              <thead><tr><th>{{ 'lic.number' | t }}</th><th>{{ 'col.customer' | t }}</th><th>{{ 'col.product' | t }}</th><th>{{ 'col.devices' | t }}</th><th>{{ 'col.expiry' | t }}</th><th>{{ 'col.status' | t }}</th></tr></thead>
              <tbody>
                @for (l of d.licenses.items; track l.id) {
                  <tr><td class="mono" dir="ltr">{{ l.licenseNumber }}</td><td>{{ l.customerName }}</td><td>{{ l.productCode }} / {{ l.planCode }}</td>
                    <td dir="ltr">{{ l.activeActivations }} / {{ l.maxActivations ?? '∞' }}</td><td>{{ l.expiresAt ? (l.expiresAt | date2) : ('common.lifetime' | t) }}</td>
                    <td><app-status [value]="l.status" /></td></tr>
                } @empty { <tr><td colspan="6" class="muted">{{ 'common.empty' | t }}</td></tr> }
              </tbody>
            </table></div></div>
          }
        }
      }
    </app-state>
  `,
})
export class TenantDetailsPage implements OnInit {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private i18n = inject(I18n);
  readonly auth = inject(AuthService);
  readonly id = input.required<string>();

  readonly tabs: { id: Tab; label: string }[] = [
    { id: 'overview', label: 'td.overview' }, { id: 'users', label: 'col.users' }, { id: 'customers', label: 'nav.customers' },
    { id: 'subscriptions', label: 'nav.subscriptions' }, { id: 'licenses', label: 'nav.licenses' },
  ];
  readonly tab = signal<Tab>('overview');

  // A platform admin reads one tenant's data through the explicit X-Tenant-Id scope.
  readonly data = loader<TenantData>(() => forkJoin({
    tenant: this.api.scoped<Tenant>(this.id(), `tenants/${this.id()}`),
    overview: this.api.overview(this.id()),
    products: this.api.scoped<Paged<Product>>(this.id(), 'products', { pageSize: 50 }),
    users: this.api.scoped<Paged<User>>(this.id(), 'users', { pageSize: 100 }),
    customers: this.api.scoped<Paged<Customer>>(this.id(), 'customers', { pageSize: 100 }),
    subscriptions: this.api.scoped<Paged<Subscription>>(this.id(), 'subscriptions', { pageSize: 100 }),
    licenses: this.api.scoped<Paged<License>>(this.id(), 'licenses', { pageSize: 100 }),
  }), false);

  ngOnInit() { this.data.load(); }

  readonly devices = computed(() => (this.data.data()?.licenses.items ?? []).reduce((s, l) => s + l.activeActivations, 0));
  readonly maxLicenses = computed(() => Math.max(1, ...(this.data.data()?.products.items ?? []).map(p => p.licenses)));
  readonly subSlices = computed(() => {
    const items = this.data.data()?.subscriptions.items ?? [];
    const colors: Record<string, string> = { Active: '#2563eb', Trial: '#f59e0b', Expired: '#ef4444', Suspended: '#94a3b8', Cancelled: '#64748b' };
    return ['Active', 'Trial', 'Expired', 'Suspended', 'Cancelled'].map(s => ({ label: s, value: items.filter(x => x.status === s).length, color: colors[s] }));
  });

  tone(i: number) { return ['blue', 'green', 'violet', 'orange'][i % 4]; }

  suspend(t: Tenant) {
    const reason = prompt(this.i18n.t('tenants.suspendReason')) ?? '';
    this.api.suspendTenant(t.id, reason).subscribe(() => { this.toasts.success(this.i18n.t('common.saved')); this.data.load(); });
  }

  resume(t: Tenant) {
    this.api.resumeTenant(t.id).subscribe(() => { this.toasts.success(this.i18n.t('common.saved')); this.data.load(); });
  }
}
