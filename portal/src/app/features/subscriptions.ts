import { Component, OnInit, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Customer, Plan, SubscriptionStatus } from '../core/api.models';
import { I18n, LocalDatePipe, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Modal, PageHead, Pager, StateView, StatusBadge, loader } from '../shared/ui';
import { Icon } from '../shared/icon';

@Component({
  selector: 'app-subscriptions',
  imports: [FormsModule, RouterLink, TranslatePipe, LocalDatePipe, LocalNumberPipe, StateView, StatusBadge, Pager, Modal, PageHead, Icon],
  template: `
    <app-page-head title="subs.title" en="Subscriptions" subtitle="subs.sub">
      @if (auth.can(auth.perm.SubscriptionsManage)) {
        <button class="btn btn-primary" type="button" (click)="openNew()"><app-icon name="plus" [size]="16" />{{ 'subs.new' | t }}</button>
      }
    </app-page-head>

    <div class="filters">
      <div class="search">
        <app-icon name="search" [size]="16" />
        <input type="search" [placeholder]="'common.search' | t" [ngModel]="search()" (ngModelChange)="search.set($event); page.set(1)" />
      </div>
      <select [ngModel]="status()" (ngModelChange)="status.set($event); page.set(1)" [attr.aria-label]="'common.status' | t">
        <option value="">{{ 'common.all' | t }}</option>
        @for (s of statuses; track s) { <option [value]="s">{{ 'status.' + s | t }}</option> }
      </select>
    </div>

    <app-state [loading]="list.loading() && !list.data()" [error]="list.error()" [empty]="list.data()?.total === 0" (retry)="list.load()">
      <div class="table-wrap">
        <table>
          <thead><tr>
            <th>{{ 'common.customer' | t }}</th><th>{{ 'common.product' | t }}</th><th>{{ 'common.plan' | t }}</th>
            <th>{{ 'common.status' | t }}</th><th>{{ 'subs.start' | t }}</th><th>{{ 'subs.end' | t }}</th><th>{{ 'subs.licenses' | t }}</th>
          </tr></thead>
          <tbody>
            @for (s of list.data()?.items; track s.id) {
              <tr class="clickable" (click)="router.navigate(['/subscriptions', s.id])">
                <td><a [routerLink]="['/subscriptions', s.id]">{{ s.customerName }}</a></td>
                <td>{{ s.productName }}</td>
                <td>{{ s.planName }} <small class="muted">v{{ s.planVersion }}</small></td>
                <td><app-status [value]="s.status" /></td>
                <td>{{ s.startDate | date2 }}</td>
                <td>{{ s.isLifetime ? ('common.lifetime' | t) : (s.endDate | date2) }}</td>
                <td>{{ s.licenses | num }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <app-pager [(page)]="page" [total]="list.data()?.total ?? 0" />
    </app-state>

    <app-modal [(open)]="open" [title]="'subs.new' | t">
      <form class="form" (ngSubmit)="create()">
        <label class="field"><span>{{ 'common.customer' | t }}</span>
          <select name="customer" required [(ngModel)]="form.customerId">
            @for (c of customers(); track c.id) { <option [value]="c.id">{{ c.name }}</option> }
          </select>
        </label>
        <label class="field"><span>{{ 'common.plan' | t }}</span>
          <select name="plan" required [(ngModel)]="form.planId">
            @for (p of plans(); track p.id) {
              <option [value]="p.id">{{ p.productName }} — {{ p.name }} ({{ p.price }} {{ p.currency }})</option>
            }
          </select>
        </label>
        <label class="field"><span>{{ 'subs.start' | t }} <small class="muted">({{ 'common.optional' | t }})</small></span>
          <input name="start" type="date" [(ngModel)]="form.startDate" /></label>
        <label class="field"><span>{{ 'subs.notes' | t }}</span><textarea name="notes" rows="2" [(ngModel)]="form.notes"></textarea></label>
        <div class="form-actions"><button class="btn btn-primary" type="submit" [disabled]="!form.customerId || !form.planId">{{ 'common.save' | t }}</button></div>
      </form>
    </app-modal>
  `,
})
export class SubscriptionsPage implements OnInit {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private i18n = inject(I18n);
  private route = inject(ActivatedRoute);
  readonly router = inject(Router);
  readonly auth = inject(AuthService);

  readonly statuses: SubscriptionStatus[] = ['Active', 'Trial', 'Suspended', 'Expired', 'Cancelled'];
  readonly search = signal('');
  readonly status = signal<SubscriptionStatus | ''>('');
  readonly page = signal(1);
  readonly list = loader(() => this.api.subscriptions({
    search: this.search(), status: this.status(), page: this.page(),
    customerId: this.route.snapshot.queryParamMap.get('customerId'),
  }), false);

  readonly open = signal(false);
  readonly customers = signal<Customer[]>([]);
  readonly plans = signal<Plan[]>([]);
  form = { customerId: '', planId: '', startDate: '', notes: '' };

  constructor() {
    effect(() => { this.search(); this.status(); this.page(); this.list.load(); });
  }

  ngOnInit() {
    if (this.route.snapshot.queryParamMap.get('new')) this.openNew(this.route.snapshot.queryParamMap.get('customerId') ?? '');
  }

  openNew(customerId = '') {
    this.form = { customerId, planId: '', startDate: '', notes: '' };
    this.api.customers({ pageSize: 200, status: 'Active' }).subscribe(r => this.customers.set(r.items));
    this.api.plans({ pageSize: 200, status: 'Published' }).subscribe(r => this.plans.set(r.items));
    this.open.set(true);
  }

  create() {
    this.api.startSubscription({
      customerId: this.form.customerId, planId: this.form.planId,
      startDate: this.form.startDate ? new Date(this.form.startDate).toISOString() : undefined, notes: this.form.notes || undefined,
    }).subscribe(r => {
      this.toasts.success(this.i18n.t('common.saved'));
      this.open.set(false);
      this.router.navigate(['/subscriptions', r.subscription.id]);
    });
  }
}
