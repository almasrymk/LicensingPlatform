import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Customer, Role, Tenant, User } from '../core/api.models';
import { I18n, LocalDatePipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Modal, PageHead, Pager, StateView, StatusBadge, loader } from '../shared/ui';
import { Icon } from '../shared/icon';

@Component({
  selector: 'app-users',
  imports: [FormsModule, TranslatePipe, LocalDatePipe, StateView, StatusBadge, Pager, Modal, PageHead, Icon],
  template: `
    <app-page-head title="users.title" en="Users" subtitle="users.sub">
      <button class="btn btn-primary" type="button" (click)="openNew()"><app-icon name="plus" [size]="16" />{{ 'users.new' | t }}</button>
    </app-page-head>

    <div class="table-card">
    <div class="filters" style="border:0;border-radius:0;box-shadow:none;border-bottom:1px solid var(--border);margin:0">
      <div class="search">
        <app-icon name="search" [size]="16" />
        <input type="search" [placeholder]="'common.search' | t" [ngModel]="search()" (ngModelChange)="search.set($event); page.set(1)" />
      </div>
      <select [ngModel]="role()" (ngModelChange)="role.set($event); page.set(1)" [attr.aria-label]="'users.role' | t">
        <option value="">{{ 'common.all' | t }}</option>
        @for (r of roles(); track r) { <option [value]="r">{{ 'role.' + r | t }}</option> }
      </select>
    </div>

    <app-state [loading]="list.loading() && !list.data()" [error]="list.error()" [empty]="list.data()?.total === 0" (retry)="list.load()">
      <div class="table-wrap">
        <table>
          <thead><tr>
            <th>{{ 'users.fullName' | t }}</th><th>{{ 'common.email' | t }}</th><th>{{ 'users.role' | t }}</th>
            @if (auth.isPlatformAdmin()) { <th>{{ 'common.tenant' | t }}</th> }
            <th>{{ 'common.customer' | t }}</th><th>{{ 'common.status' | t }}</th><th>{{ 'users.lastLogin' | t }}</th><th>{{ 'common.actions' | t }}</th>
          </tr></thead>
          <tbody>
            @for (u of list.data()?.items; track u.id) {
              <tr>
                <td>{{ u.fullName }}</td>
                <td dir="ltr">{{ u.email }}</td>
                <td>{{ 'role.' + u.role | t }}</td>
                @if (auth.isPlatformAdmin()) { <td>{{ u.tenantName ?? '—' }}</td> }
                <td>{{ u.customerName ?? '—' }}</td>
                <td><app-status [value]="u.isActive ? 'Active' : 'Disabled'" /></td>
                <td>{{ u.lastLoginAt | date2: true }}</td>
                <td class="actions">
                  <button class="btn btn-ghost" type="button" (click)="pwdTarget.set(u); newPassword = ''; pwdOpen.set(true)">{{ 'users.resetPassword' | t }}</button>
                  @if (u.id !== auth.user()?.id) {
                    <button class="btn btn-ghost" type="button" (click)="setActive(u, !u.isActive)">{{ (u.isActive ? 'users.deactivate' : 'users.activate') | t }}</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <app-pager [(page)]="page" [(pageSize)]="pageSize" [total]="list.data()?.total ?? 0" />
    </app-state>
    </div>

    <app-modal [(open)]="open" [title]="'users.new' | t">
      <form class="form" (ngSubmit)="create()">
        <label class="field"><span>{{ 'users.fullName' | t }}</span><input name="name" required [(ngModel)]="form.fullName" /></label>
        <label class="field"><span>{{ 'common.email' | t }}</span><input name="email" type="email" dir="ltr" required [(ngModel)]="form.email" /></label>
        <label class="field"><span>{{ 'login.password' | t }}</span><input name="pwd" type="password" dir="ltr" autocomplete="new-password" required [(ngModel)]="form.password" /></label>
        <label class="field"><span>{{ 'users.role' | t }}</span>
          <select name="role" required [ngModel]="form.role" (ngModelChange)="form.role = $event; onRoleChange()">
            @for (r of roles(); track r) { <option [value]="r">{{ 'role.' + r | t }}</option> }
          </select>
        </label>
        @if (auth.isPlatformAdmin() && form.role !== 'PlatformAdmin') {
          <label class="field"><span>{{ 'common.tenant' | t }}</span>
            <select name="tenant" required [ngModel]="form.tenantId" (ngModelChange)="form.tenantId = $event; loadCustomers()">
              @for (t of tenants(); track t.id) { <option [value]="t.id">{{ t.name }}</option> }
            </select>
          </label>
        }
        @if (form.role === 'CustomerUser') {
          <p class="hint">{{ 'users.customerHint' | t }}</p>
          <label class="field"><span>{{ 'common.customer' | t }}</span>
            <select name="customer" required [(ngModel)]="form.customerId">
              @for (c of customers(); track c.id) { <option [value]="c.id">{{ c.name }}</option> }
            </select>
          </label>
        }
        <div class="form-actions"><button class="btn btn-primary" type="submit">{{ 'common.save' | t }}</button></div>
      </form>
    </app-modal>

    <app-modal [(open)]="pwdOpen" [title]="('users.resetPassword' | t) + ' — ' + (pwdTarget()?.email ?? '')">
      <form class="form" (ngSubmit)="resetPassword()">
        <label class="field"><span>{{ 'users.newPassword' | t }}</span><input name="np" type="password" dir="ltr" autocomplete="new-password" required [(ngModel)]="newPassword" /></label>
        <div class="form-actions"><button class="btn btn-primary" type="submit">{{ 'common.save' | t }}</button></div>
      </form>
    </app-modal>
  `,
})
export class UsersPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private i18n = inject(I18n);
  readonly auth = inject(AuthService);

  readonly roles = computed<Role[]>(() => this.auth.isPlatformAdmin()
    ? ['PlatformAdmin', 'TenantAdmin', 'TenantOperator', 'CustomerUser']
    : ['TenantAdmin', 'TenantOperator', 'CustomerUser']);
  private route = inject(ActivatedRoute);
  readonly search = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  readonly role = signal('');
  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly list = loader(() => this.api.users({ search: this.search(), role: this.role(), page: this.page(), pageSize: this.pageSize() }), false);

  readonly open = signal(false);
  readonly tenants = signal<Tenant[]>([]);
  readonly customers = signal<Customer[]>([]);
  form = { fullName: '', email: '', password: '', role: 'TenantOperator' as Role, tenantId: '', customerId: '' };

  readonly pwdOpen = signal(false);
  readonly pwdTarget = signal<User | null>(null);
  newPassword = '';

  constructor() {
    effect(() => { this.search(); this.role(); this.page(); this.pageSize(); this.list.load(); });
  }

  private ok() { this.toasts.success(this.i18n.t('common.saved')); }

  openNew() {
    this.form = { fullName: '', email: '', password: '', role: 'TenantOperator', tenantId: this.auth.tenantScope() ?? '', customerId: '' };
    if (this.auth.isPlatformAdmin()) this.api.tenants({ pageSize: 200 }).subscribe(r => this.tenants.set(r.items));
    this.loadCustomers();
    this.open.set(true);
  }

  onRoleChange() { if (this.form.role === 'CustomerUser') this.loadCustomers(); }

  loadCustomers() {
    this.api.customers({ pageSize: 200 }).subscribe(r => this.customers.set(
      r.items.filter(c => !this.form.tenantId || c.tenantId === this.form.tenantId)));
  }

  create() {
    this.api.createUser({
      email: this.form.email, fullName: this.form.fullName, password: this.form.password, role: this.form.role,
      tenantId: this.form.tenantId || undefined, customerId: this.form.role === 'CustomerUser' ? this.form.customerId : undefined,
    }).subscribe(() => { this.ok(); this.open.set(false); this.list.load(); });
  }

  setActive(u: User, active: boolean) {
    this.api.setUserActive(u.id, active).subscribe(() => { this.ok(); this.list.load(); });
  }

  resetPassword() {
    const u = this.pwdTarget();
    if (!u) return;
    this.api.resetPassword(u.id, this.newPassword).subscribe(() => { this.ok(); this.pwdOpen.set(false); });
  }
}
