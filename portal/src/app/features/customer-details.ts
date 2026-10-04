import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { I18n, LocalDatePipe, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Modal, StateView, StatusBadge, loader } from '../shared/ui';

/** Customer 360: profile, contacts, subscriptions, licenses and (for staff) the customer's portal users. */
@Component({
  selector: 'app-customer-details',
  imports: [FormsModule, RouterLink, TranslatePipe, LocalDatePipe, LocalNumberPipe, StateView, StatusBadge, Modal],
  template: `
    <app-state [loading]="data.loading() && !data.data()" [error]="data.error()" (retry)="data.load()">
      @if (data.data(); as d) {
        <header class="page-head">
          <div>
            <a class="back" routerLink="/customers">← {{ 'common.back' | t }}</a>
            <h1>{{ d.c.customer.name }} <app-status [value]="d.c.customer.status" /></h1>
            <p class="muted">{{ d.c.customer.tenantName }}</p>
          </div>
          @if (auth.can(auth.perm.CustomersManage)) {
            <div class="actions">
              <button class="btn" type="button" (click)="startEdit()">{{ 'common.edit' | t }}</button>
              @if (d.c.customer.status === 'Active') {
                <button class="btn btn-ghost" type="button" (click)="setStatus('Inactive')">{{ 'customers.deactivate' | t }}</button>
              } @else {
                <button class="btn btn-ghost" type="button" (click)="setStatus('Active')">{{ 'customers.activate' | t }}</button>
              }
            </div>
          }
        </header>

        <div class="grid-2">
          <section class="card">
            <h2>{{ 'common.details' | t }}</h2>
            <dl class="dl">
              <dt>{{ 'common.email' | t }}</dt><dd dir="ltr">{{ d.c.customer.email ?? '—' }}</dd>
              <dt>{{ 'common.phone' | t }}</dt><dd dir="ltr">{{ d.c.customer.phone ?? '—' }}</dd>
              <dt>{{ 'common.country' | t }}</dt><dd>{{ d.c.customer.country ?? '—' }}</dd>
              <dt>{{ 'customers.taxNumber' | t }}</dt><dd dir="ltr">{{ d.c.customer.taxNumber ?? '—' }}</dd>
              <dt>{{ 'common.createdAt' | t }}</dt><dd>{{ d.c.customer.createdAt | date2 }}</dd>
            </dl>
          </section>

          <section class="card">
            <div class="card-head">
              <h2>{{ 'customers.contacts' | t }}</h2>
              @if (auth.can(auth.perm.CustomersManage)) {
                <button class="btn btn-ghost" type="button" (click)="contactOpen.set(true)">{{ 'customers.addContact' | t }}</button>
              }
            </div>
            @for (c of d.c.contacts; track c.id) {
              <div class="contact">
                <div>
                  <strong>{{ c.name }}</strong> @if (c.isPrimary) { <span class="badge info">{{ 'customers.primary' | t }}</span> }
                  <br /><small class="muted">{{ c.jobTitle }} <span dir="ltr">{{ c.email }} {{ c.phone }}</span></small>
                </div>
                @if (auth.can(auth.perm.CustomersManage)) {
                  <button class="btn btn-ghost icon" type="button" aria-label="remove" (click)="removeContact(c.id)">✕</button>
                }
              </div>
            } @empty { <p class="muted">{{ 'common.empty' | t }}</p> }
          </section>
        </div>

        <section class="card">
          <div class="card-head">
            <h2>{{ 'nav.subscriptions' | t }}</h2>
            @if (auth.can(auth.perm.SubscriptionsManage)) {
              <a class="btn btn-ghost" routerLink="/subscriptions" [queryParams]="{ new: 1, customerId: id() }">{{ 'subs.new' | t }}</a>
            }
          </div>
          <div class="table-wrap">
            <table>
              <thead><tr><th>{{ 'common.product' | t }}</th><th>{{ 'common.plan' | t }}</th><th>{{ 'common.status' | t }}</th><th>{{ 'subs.start' | t }}</th><th>{{ 'subs.end' | t }}</th></tr></thead>
              <tbody>
                @for (s of d.subs.items; track s.id) {
                  <tr><td><a [routerLink]="['/subscriptions', s.id]">{{ s.productName }}</a></td><td>{{ s.planName }}</td>
                    <td><app-status [value]="s.status" /></td><td>{{ s.startDate | date2 }}</td>
                    <td>{{ s.isLifetime ? ('common.lifetime' | t) : (s.endDate | date2) }}</td></tr>
                } @empty { <tr><td colspan="5" class="muted">{{ 'common.empty' | t }}</td></tr> }
              </tbody>
            </table>
          </div>
        </section>

        <section class="card">
          <h2>{{ 'nav.licenses' | t }}</h2>
          <div class="table-wrap">
            <table>
              <thead><tr><th>{{ 'lic.number' | t }}</th><th>{{ 'common.product' | t }}</th><th>{{ 'common.status' | t }}</th><th>{{ 'lic.devices' | t }}</th><th>{{ 'lic.expires' | t }}</th></tr></thead>
              <tbody>
                @for (l of d.licenses.items; track l.id) {
                  <tr><td><a [routerLink]="['/licenses', l.id]" dir="ltr">{{ l.licenseNumber }}</a></td><td>{{ l.productCode }} / {{ l.planCode }}</td>
                    <td><app-status [value]="l.status" /></td>
                    <td>{{ l.activeActivations | num }} / {{ l.maxActivations ?? '∞' }}</td>
                    <td>{{ l.expiresAt ? (l.expiresAt | date2) : ('common.lifetime' | t) }}</td></tr>
                } @empty { <tr><td colspan="5" class="muted">{{ 'common.empty' | t }}</td></tr> }
              </tbody>
            </table>
          </div>
        </section>

        @if (auth.can(auth.perm.UsersManage)) {
          <section class="card">
            <div class="card-head">
              <h2>{{ 'customers.users' | t }}</h2>
              <button class="btn btn-ghost" type="button" (click)="userOpen.set(true)">{{ 'customers.addUser' | t }}</button>
            </div>
            <p class="hint">{{ 'users.customerHint' | t }}</p>
            @if (users.data(); as u) {
              <ul class="plain">
                @for (x of u.items; track x.id) {
                  @if (x.customerId === id()) { <li><span dir="ltr">{{ x.email }}</span> — {{ x.fullName }} <app-status [value]="x.isActive ? 'Active' : 'Disabled'" /></li> }
                }
              </ul>
            }
          </section>
        }
      }
    </app-state>

    <app-modal [(open)]="editOpen" [title]="'common.edit' | t">
      <form class="form" (ngSubmit)="saveEdit()">
        <label class="field"><span>{{ 'common.name' | t }}</span><input name="name" required [(ngModel)]="form.name" /></label>
        <div class="grid-2">
          <label class="field"><span>{{ 'common.email' | t }}</span><input name="email" type="email" dir="ltr" [(ngModel)]="form.email" /></label>
          <label class="field"><span>{{ 'common.phone' | t }}</span><input name="phone" dir="ltr" [(ngModel)]="form.phone" /></label>
          <label class="field"><span>{{ 'common.country' | t }}</span><input name="country" [(ngModel)]="form.country" /></label>
          <label class="field"><span>{{ 'customers.taxNumber' | t }}</span><input name="tax" dir="ltr" [(ngModel)]="form.taxNumber" /></label>
        </div>
        <div class="form-actions"><button class="btn btn-primary" type="submit">{{ 'common.save' | t }}</button></div>
      </form>
    </app-modal>

    <app-modal [(open)]="contactOpen" [title]="'customers.addContact' | t">
      <form class="form" (ngSubmit)="addContact()">
        <label class="field"><span>{{ 'common.name' | t }}</span><input name="cname" required [(ngModel)]="contact.name" /></label>
        <div class="grid-2">
          <label class="field"><span>{{ 'common.email' | t }}</span><input name="cemail" type="email" dir="ltr" [(ngModel)]="contact.email" /></label>
          <label class="field"><span>{{ 'common.phone' | t }}</span><input name="cphone" dir="ltr" [(ngModel)]="contact.phone" /></label>
        </div>
        <label class="field"><span>{{ 'customers.jobTitle' | t }}</span><input name="job" [(ngModel)]="contact.jobTitle" /></label>
        <label class="check"><input type="checkbox" name="primary" [(ngModel)]="contact.isPrimary" /> {{ 'customers.primary' | t }}</label>
        <div class="form-actions"><button class="btn btn-primary" type="submit">{{ 'common.save' | t }}</button></div>
      </form>
    </app-modal>

    <app-modal [(open)]="userOpen" [title]="'customers.addUser' | t">
      <form class="form" (ngSubmit)="addUser()">
        <p class="hint">{{ 'users.customerHint' | t }}</p>
        <label class="field"><span>{{ 'users.fullName' | t }}</span><input name="uname" required [(ngModel)]="user.fullName" /></label>
        <label class="field"><span>{{ 'common.email' | t }}</span><input name="uemail" type="email" dir="ltr" required [(ngModel)]="user.email" /></label>
        <label class="field"><span>{{ 'login.password' | t }}</span><input name="upass" type="password" dir="ltr" required autocomplete="new-password" [(ngModel)]="user.password" /></label>
        <div class="form-actions"><button class="btn btn-primary" type="submit">{{ 'common.save' | t }}</button></div>
      </form>
    </app-modal>
  `,
})
export class CustomerDetailsPage implements OnInit {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private i18n = inject(I18n);
  readonly auth = inject(AuthService);
  readonly id = input.required<string>();

  readonly data = loader(() => forkJoin({
    c: this.api.customer(this.id()),
    subs: this.api.subscriptions({ customerId: this.id(), pageSize: 100 }),
    licenses: this.api.licenses({ customerId: this.id(), pageSize: 100 }),
  }), false);
  readonly users = loader(() => this.api.users({ role: 'CustomerUser', pageSize: 200 }), this.auth.can(this.auth.perm.UsersManage));

  readonly editOpen = signal(false);
  readonly contactOpen = signal(false);
  readonly userOpen = signal(false);
  form = { name: '', email: '', phone: '', country: '', taxNumber: '' };
  contact = { name: '', email: '', phone: '', jobTitle: '', isPrimary: false };
  user = { fullName: '', email: '', password: '' };

  ngOnInit() { this.data.load(); }

  private ok() { this.toasts.success(this.i18n.t('common.saved')); }

  startEdit() {
    const c = this.data.data()!.c.customer;
    this.form = { name: c.name, email: c.email ?? '', phone: c.phone ?? '', country: c.country ?? '', taxNumber: c.taxNumber ?? '' };
    this.editOpen.set(true);
  }

  saveEdit() {
    this.api.updateCustomer(this.id(), { ...this.form, email: this.form.email || undefined })
      .subscribe(() => { this.ok(); this.editOpen.set(false); this.data.load(); });
  }

  setStatus(status: 'Active' | 'Inactive') {
    this.api.setCustomerStatus(this.id(), status).subscribe(() => { this.ok(); this.data.load(); });
  }

  addContact() {
    this.api.addContact(this.id(), { ...this.contact, email: this.contact.email || undefined }).subscribe(() => {
      this.ok(); this.contactOpen.set(false); this.contact = { name: '', email: '', phone: '', jobTitle: '', isPrimary: false }; this.data.load();
    });
  }

  removeContact(contactId: string) {
    if (!confirm(this.i18n.t('common.confirm'))) return;
    this.api.removeContact(this.id(), contactId).subscribe(() => { this.ok(); this.data.load(); });
  }

  addUser() {
    const c = this.data.data()!.c.customer;
    this.api.createUser({ ...this.user, role: 'CustomerUser', customerId: c.id, tenantId: c.tenantId }).subscribe(() => {
      this.ok(); this.userOpen.set(false); this.user = { fullName: '', email: '', password: '' }; this.users.load();
    });
  }
}
