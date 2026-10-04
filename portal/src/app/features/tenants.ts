import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Tenant } from '../core/api.models';
import { I18n, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Modal, PageHead, Pager, StateView, StatusBadge, loader } from '../shared/ui';
import { Icon } from '../shared/icon';
import { ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-tenants',
  imports: [FormsModule, TranslatePipe, LocalNumberPipe, StateView, StatusBadge, Pager, Modal, PageHead, Icon],
  template: `
    <app-page-head title="tenants.title" en="Tenants Management" subtitle="tenants.sub">
      @if (auth.can(auth.perm.TenantsManage)) {
        <button class="btn btn-primary" type="button" (click)="openNew()"><app-icon name="plus" [size]="16" />{{ 'tenants.new' | t }}</button>
      }
    </app-page-head>

    <div class="filters">
      <div class="search">
        <app-icon name="search" [size]="16" />
        <input type="search" [placeholder]="'tenants.searchPh' | t" [ngModel]="search()" (ngModelChange)="search.set($event); page.set(1)" />
      </div>
      <select [ngModel]="status()" (ngModelChange)="status.set($event); page.set(1)" [attr.aria-label]="'common.status' | t">
        <option value="">{{ 'common.all' | t }} — {{ 'common.status' | t }}</option>
        <option value="Active">{{ 'status.Active' | t }}</option>
        <option value="Suspended">{{ 'status.Suspended' | t }}</option>
      </select>
    </div>

    <app-state [loading]="list.loading() && !list.data()" [error]="list.error()" [empty]="list.data()?.total === 0" (retry)="list.load()">
      <div class="table-wrap">
        <table>
          <thead><tr>
            <th>{{ 'tenants.name' | t }}</th><th>{{ 'tenants.users' | t }}</th><th>{{ 'nav.customers' | t }}</th>
            <th>{{ 'tenants.licenses' | t }}</th><th>{{ 'common.status' | t }}</th><th>{{ 'common.actions' | t }}</th>
          </tr></thead>
          <tbody>
            @for (t of list.data()?.items; track t.id) {
              <tr>
                <td><strong>{{ t.name }}</strong><span class="cell-sub mono" dir="ltr">{{ t.code }}</span></td>
                <td>{{ t.users | num }}</td>
                <td>{{ t.customers | num }}</td>
                <td>{{ t.activeLicenses | num }}</td>
                <td><app-status [value]="t.status" />
                  @if (t.suspensionReason) { <span class="cell-sub">{{ t.suspensionReason }}</span> }</td>
                <td class="actions">
                  <button class="btn btn-sm" type="button" (click)="auth.setTenantScope(t.id)">{{ 'tenants.open' | t }}</button>
                  @if (auth.can(auth.perm.TenantsManage)) {
                    <button class="btn btn-sm" type="button" (click)="edit(t)">{{ 'common.edit' | t }}</button>
                    @if (t.status === 'Active') {
                      <button class="btn btn-sm btn-danger" type="button" (click)="suspendTarget.set(t); suspendOpen.set(true)">{{ 'tenants.suspend' | t }}</button>
                    } @else {
                      <button class="btn btn-sm" type="button" (click)="resume(t)">{{ 'customers.activate' | t }}</button>
                    }
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <app-pager [(page)]="page" [total]="list.data()?.total ?? 0" />
    </app-state>

    <app-modal [(open)]="formOpen" [title]="(editing() ? 'common.edit' : 'tenants.new') | t">
      <form class="form" (ngSubmit)="save()">
        <label class="field"><span>{{ 'common.name' | t }}</span><input name="name" required [(ngModel)]="form.name" /></label>
        <label class="field"><span>{{ 'common.code' | t }}</span><input name="code" dir="ltr" required [disabled]="!!editing()" [(ngModel)]="form.code" /></label>
        <label class="field"><span>{{ 'tenants.contactEmail' | t }}</span><input name="email" type="email" dir="ltr" [(ngModel)]="form.contactEmail" /></label>
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">{{ 'common.save' | t }}</button>
          <button class="btn btn-ghost" type="button" (click)="formOpen.set(false)">{{ 'common.cancel' | t }}</button>
        </div>
      </form>
    </app-modal>

    <app-modal [(open)]="suspendOpen" [title]="('tenants.suspend' | t) + ' — ' + (suspendTarget()?.name ?? '')">
      <form class="form" (ngSubmit)="suspend()">
        <p class="hint">{{ 'tenants.suspendHint' | t }}</p>
        <label class="field"><span>{{ 'tenants.suspendReason' | t }}</span><input name="reason" [(ngModel)]="reason" /></label>
        <div class="form-actions">
          <button class="btn btn-danger" type="submit">{{ 'tenants.suspend' | t }}</button>
          <button class="btn btn-ghost" type="button" (click)="suspendOpen.set(false)">{{ 'common.cancel' | t }}</button>
        </div>
      </form>
    </app-modal>
  `,
})
export class TenantsPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private i18n = inject(I18n);
  readonly auth = inject(AuthService);

  private route = inject(ActivatedRoute);
  readonly search = signal('');
  readonly status = signal('');
  readonly page = signal(1);
  readonly list = loader(() => this.api.tenants({ search: this.search(), status: this.status(), page: this.page(), pageSize: 20 }), false);

  readonly formOpen = signal(false);
  readonly editing = signal<Tenant | null>(null);
  form = { name: '', code: '', contactEmail: '' };

  readonly suspendOpen = signal(false);
  readonly suspendTarget = signal<Tenant | null>(null);
  reason = '';

  constructor() {
    effect(() => { this.search(); this.status(); this.page(); this.list.load(); });
    if (this.route.snapshot.queryParamMap.get('new')) queueMicrotask(() => this.openNew());
  }

  openNew() { this.editing.set(null); this.form = { name: '', code: '', contactEmail: '' }; this.formOpen.set(true); }
  edit(t: Tenant) { this.editing.set(t); this.form = { name: t.name, code: t.code, contactEmail: t.contactEmail ?? '' }; this.formOpen.set(true); }

  save() {
    const e = this.editing();
    const req = e
      ? this.api.updateTenant(e.id, { name: this.form.name, contactEmail: this.form.contactEmail || undefined })
      : this.api.createTenant({ name: this.form.name, code: this.form.code, contactEmail: this.form.contactEmail || undefined });
    req.subscribe(() => { this.toasts.success(this.i18n.t('common.saved')); this.formOpen.set(false); this.list.load(); });
  }

  suspend() {
    const t = this.suspendTarget();
    if (!t) return;
    this.api.suspendTenant(t.id, this.reason).subscribe(() => {
      this.toasts.success(this.i18n.t('common.saved')); this.suspendOpen.set(false); this.reason = ''; this.list.load();
    });
  }

  resume(t: Tenant) {
    this.api.resumeTenant(t.id).subscribe(() => { this.toasts.success(this.i18n.t('common.saved')); this.list.load(); });
  }
}
