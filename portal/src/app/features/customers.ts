import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { CustomerStatus } from '../core/api.models';
import { I18n, LocalDatePipe, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Modal, PageHead, Pager, StateView, StatusBadge, loader } from '../shared/ui';
import { Icon } from '../shared/icon';

@Component({
  selector: 'app-customers',
  imports: [FormsModule, RouterLink, TranslatePipe, LocalDatePipe, LocalNumberPipe, StateView, StatusBadge, Pager, Modal, PageHead, Icon],
  template: `
    <app-page-head title="customers.title" en="Customers" subtitle="customers.sub">
      @if (auth.can(auth.perm.CustomersManage)) {
        <button class="btn btn-primary" type="button" (click)="open.set(true)"><app-icon name="plus" [size]="16" />{{ 'customers.new' | t }}</button>
      }
    </app-page-head>

    <div class="filters">
      <div class="search">
        <app-icon name="search" [size]="16" />
        <input type="search" [placeholder]="'customers.searchPh' | t" [ngModel]="search()" (ngModelChange)="search.set($event); page.set(1)" />
      </div>
      <select [ngModel]="status()" (ngModelChange)="status.set($event); page.set(1)" [attr.aria-label]="'common.status' | t">
        <option value="">{{ 'common.all' | t }}</option>
        <option value="Active">{{ 'status.Active' | t }}</option>
        <option value="Inactive">{{ 'status.Inactive' | t }}</option>
      </select>
    </div>

    <app-state [loading]="list.loading() && !list.data()" [error]="list.error()" [empty]="list.data()?.total === 0" (retry)="list.load()">
      <div class="table-wrap">
        <table>
          <thead><tr>
            <th>{{ 'common.name' | t }}</th>
            @if (auth.isPlatformAdmin()) { <th>{{ 'common.tenant' | t }}</th> }
            <th>{{ 'common.email' | t }}</th><th>{{ 'common.country' | t }}</th><th>{{ 'common.status' | t }}</th>
            <th>{{ 'customers.activeSubs' | t }}</th><th>{{ 'customers.activeLicenses' | t }}</th><th>{{ 'common.createdAt' | t }}</th>
          </tr></thead>
          <tbody>
            @for (c of list.data()?.items; track c.id) {
              <tr class="clickable" (click)="router.navigate(['/customers', c.id])">
                <td><a [routerLink]="['/customers', c.id]">{{ c.name }}</a></td>
                @if (auth.isPlatformAdmin()) { <td>{{ c.tenantName }}</td> }
                <td dir="ltr">{{ c.email ?? '—' }}</td>
                <td>{{ c.country ?? '—' }}</td>
                <td><app-status [value]="c.status" /></td>
                <td>{{ c.activeSubscriptions | num }}</td>
                <td>{{ c.activeLicenses | num }}</td>
                <td>{{ c.createdAt | date2 }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <app-pager [(page)]="page" [total]="list.data()?.total ?? 0" />
    </app-state>

    <app-modal [(open)]="open" [title]="'customers.new' | t">
      <form class="form" (ngSubmit)="create()">
        <label class="field"><span>{{ 'common.name' | t }}</span><input name="name" required [(ngModel)]="form.name" /></label>
        <div class="grid-2">
          <label class="field"><span>{{ 'common.email' | t }}</span><input name="email" type="email" dir="ltr" [(ngModel)]="form.email" /></label>
          <label class="field"><span>{{ 'common.phone' | t }}</span><input name="phone" dir="ltr" [(ngModel)]="form.phone" /></label>
          <label class="field"><span>{{ 'common.country' | t }}</span><input name="country" [(ngModel)]="form.country" /></label>
          <label class="field"><span>{{ 'customers.taxNumber' | t }}</span><input name="tax" dir="ltr" [(ngModel)]="form.taxNumber" /></label>
        </div>
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">{{ 'common.save' | t }}</button>
          <button class="btn btn-ghost" type="button" (click)="open.set(false)">{{ 'common.cancel' | t }}</button>
        </div>
      </form>
    </app-modal>
  `,
})
export class CustomersPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private i18n = inject(I18n);
  readonly router = inject(Router);
  readonly auth = inject(AuthService);

  readonly search = signal('');
  readonly status = signal<CustomerStatus | ''>('');
  readonly page = signal(1);
  readonly list = loader(() => this.api.customers({ search: this.search(), status: this.status(), page: this.page() }), false);
  readonly open = signal(false);
  form = { name: '', email: '', phone: '', country: '', taxNumber: '' };

  constructor() {
    effect(() => { this.search(); this.status(); this.page(); this.list.load(); });
  }

  create() {
    this.api.createCustomer({ ...this.form, email: this.form.email || undefined }).subscribe(r => {
      this.toasts.success(this.i18n.t('common.saved'));
      this.open.set(false);
      this.router.navigate(['/customers', r.customer.id]);
    });
  }
}
