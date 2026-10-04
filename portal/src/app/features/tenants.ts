import { Component, HostListener, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Tenant } from '../core/api.models';
import { I18n, LocalDatePipe, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Icon } from '../shared/icon';
import { Modal, Pager, StateView, StatusBadge, loader } from '../shared/ui';
import { Avatar, ImageUpload, Lookups } from '../shared/media';

/** Tenants list from the mockup: search + filters bar, table with row actions menu, numbered pagination. */
@Component({
  selector: 'app-tenants',
  imports: [FormsModule, RouterLink, TranslatePipe, LocalNumberPipe, LocalDatePipe, StateView, StatusBadge, Pager, Modal, Icon, Avatar, ImageUpload],
  template: `
    <header class="page-head">
      <div><h1>{{ 'tenants.title' | t }}</h1><p class="subtitle">{{ 'tenants.sub' | t }}</p></div>
      @if (auth.can(auth.perm.TenantsManage)) {
        <button class="btn btn-primary" type="button" (click)="openNew()"><app-icon name="plus" [size]="16" />{{ 'tenants.new' | t }}</button>
      }
    </header>

    <div class="table-card">
      <div class="filters" style="border:0;border-radius:0;box-shadow:none;border-bottom:1px solid var(--border);margin:0">
        <div class="search">
          <app-icon name="search" [size]="16" />
          <input type="search" [placeholder]="'tenants.searchPh' | t" [ngModel]="search()" (ngModelChange)="search.set($event); page.set(1)" />
        </div>
        <select [ngModel]="status()" (ngModelChange)="status.set($event); page.set(1)" [attr.aria-label]="'col.status' | t">
          <option value="">{{ 'filter.allStatus' | t }}</option>
          <option value="Active">{{ 'status.Active' | t }}</option>
          <option value="Suspended">{{ 'status.Suspended' | t }}</option>
        </select>
        <label class="date-pair"><span>{{ 'filter.createdFrom' | t }}</span><input type="date" [ngModel]="from()" (ngModelChange)="from.set($event); page.set(1)" />
          <span>{{ 'filter.to' | t }}</span><input type="date" [ngModel]="to()" (ngModelChange)="to.set($event); page.set(1)" /></label>
        @if (status() || from() || to() || search()) {
          <button class="btn btn-sm btn-ghost" type="button" (click)="clear()">{{ 'filter.clear' | t }}</button>
        }
        <span class="spacer"></span>
        <button class="icon-btn" type="button" [attr.aria-label]="'common.refresh' | t" [title]="'common.refresh' | t" (click)="list.load()"><app-icon name="refresh" [size]="18" /></button>
      </div>

      <app-state [loading]="list.loading() && !list.data()" [error]="list.error()" [empty]="list.data()?.total === 0" (retry)="list.load()">
        <div class="table-wrap">
          <table>
            <thead><tr>
              <th>{{ 'col.name' | t }}</th><th>{{ 'col.code' | t }}</th><th>{{ 'col.users' | t }}</th><th>{{ 'col.products' | t }}</th>
              <th>{{ 'col.licenses' | t }}</th><th>{{ 'col.status' | t }}</th><th>{{ 'col.createdAt' | t }}</th><th>{{ 'col.actions' | t }}</th>
            </tr></thead>
            <tbody>
              @for (t of list.data()?.items; track t.id) {
                <tr class="clickable" (click)="router.navigate(['/tenants', t.id])">
                  <td><div class="name-cell"><app-avatar [src]="t.imageUrl" [name]="t.name" [size]="32" /><strong>{{ t.name }}</strong></div></td>
                  <td class="mono" dir="ltr">{{ t.code }}</td>
                  <td>{{ t.users | num }}</td>
                  <td>{{ t.products | num }}</td>
                  <td>{{ t.licenses | num }}</td>
                  <td><app-status [value]="t.status" /></td>
                  <td>{{ t.createdAt | date2 }}</td>
                  <td (click)="$event.stopPropagation()">
                    <div class="row-menu">
                      <button class="kebab" type="button" [attr.aria-label]="'col.actions' | t" (click)="toggleMenu(t.id, $event)"><app-icon name="moreH" /></button>
                      @if (menuFor() === t.id) {
                        <div class="menu" role="menu">
                          <a role="menuitem" [routerLink]="['/tenants', t.id]"><app-icon name="eye" [size]="16" />{{ 'common.open' | t }}</a>
                          <button role="menuitem" type="button" (click)="auth.setTenantScope(t.id)"><app-icon name="building" [size]="16" />{{ 'td.viewData' | t }}</button>
                          @if (auth.can(auth.perm.TenantsManage)) {
                            <button role="menuitem" type="button" (click)="edit(t)"><app-icon name="edit" [size]="16" />{{ 'common.edit' | t }}</button>
                            @if (t.status === 'Active') {
                              <button role="menuitem" type="button" class="danger" (click)="suspendTarget.set(t); suspendOpen.set(true)"><app-icon name="ban" [size]="16" />{{ 'tenants.suspend' | t }}</button>
                            } @else {
                              <button role="menuitem" type="button" (click)="resume(t)"><app-icon name="checkCircle" [size]="16" />{{ 'customers.activate' | t }}</button>
                            }
                          }
                        </div>
                      }
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <app-pager [(page)]="page" [(pageSize)]="pageSize" [total]="list.data()?.total ?? 0" />
      </app-state>
    </div>

    <app-modal [(open)]="formOpen" [title]="(editing() ? 'common.edit' : 'tenants.new') | t">
      <form class="form" (ngSubmit)="save()">
        @if (editing(); as e) {
          <div class="field"><span>{{ 'img.title' | t }}</span><app-image-upload owner="tenants" [ownerId]="e.id" [url]="e.imageUrl" [name]="e.name" (changed)="list.load(); lookups.invalidate()" /></div>
        } @else {
          <p class="hint">{{ 'img.afterSave' | t }}</p>
        }
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
          <button class="btn btn-solid-danger" type="submit">{{ 'tenants.suspend' | t }}</button>
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
  private route = inject(ActivatedRoute);
  readonly router = inject(Router);
  readonly auth = inject(AuthService);
  readonly lookups = inject(Lookups);

  readonly search = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  readonly status = signal(this.route.snapshot.queryParamMap.get('status') ?? '');
  readonly from = signal('');
  readonly to = signal('');
  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly list = loader(() => this.api.tenants({ search: this.search(), status: this.status(), from: this.from(), to: this.to(), page: this.page(), pageSize: this.pageSize() }), false);
  readonly menuFor = signal<string | null>(null);

  readonly formOpen = signal(false);
  readonly editing = signal<Tenant | null>(null);
  form = { name: '', code: '', contactEmail: '' };

  readonly suspendOpen = signal(false);
  readonly suspendTarget = signal<Tenant | null>(null);
  reason = '';

  constructor() {
    effect(() => { this.search(); this.status(); this.from(); this.to(); this.page(); this.pageSize(); this.list.load(); });
    if (this.route.snapshot.queryParamMap.get('new')) queueMicrotask(() => this.openNew());
  }

  clear() { this.search.set(''); this.status.set(''); this.from.set(''); this.to.set(''); this.page.set(1); }

  @HostListener('document:click') closeMenus() { this.menuFor.set(null); }
  toggleMenu(id: string, e: Event) { e.stopPropagation(); this.menuFor.set(this.menuFor() === id ? null : id); }

  openNew() { this.editing.set(null); this.form = { name: '', code: '', contactEmail: '' }; this.formOpen.set(true); }
  edit(t: Tenant) { this.editing.set(t); this.form = { name: t.name, code: t.code, contactEmail: t.contactEmail ?? '' }; this.formOpen.set(true); }

  save() {
    const e = this.editing();
    const req = e
      ? this.api.updateTenant(e.id, { name: this.form.name, contactEmail: this.form.contactEmail || undefined })
      : this.api.createTenant({ name: this.form.name, code: this.form.code, contactEmail: this.form.contactEmail || undefined });
    req.subscribe(() => { this.toasts.success(this.i18n.t('common.saved')); this.formOpen.set(false); this.list.load(); this.lookups.invalidate(); });
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
