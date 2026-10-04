import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { IssuedLicense, Plan, SubscriptionAction } from '../core/api.models';
import { I18n, LocalDatePipe, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Modal, SecretBox, StateView, StatusBadge, loader } from '../shared/ui';

@Component({
  selector: 'app-subscription-details',
  imports: [FormsModule, RouterLink, TranslatePipe, LocalDatePipe, LocalNumberPipe, StateView, StatusBadge, Modal, SecretBox],
  template: `
    <app-state [loading]="data.loading() && !data.data()" [error]="data.error()" (retry)="data.load()">
      @if (data.data(); as d) {
        @let s = d.sub.subscription;
        <header class="page-head">
          <div>
            <a class="back" routerLink="/subscriptions">← {{ 'common.back' | t }}</a>
            <h1>{{ s.productName }} — {{ s.planName }} <app-status [value]="s.status" /></h1>
            <p class="muted"><a [routerLink]="['/customers', s.customerId]">{{ s.customerName }}</a></p>
          </div>
          @if (auth.can(auth.perm.SubscriptionsManage)) {
            <div class="actions">
              @for (a of s.allowedActions; track a) {
                @if (a !== 'Expire') {
                  <button class="btn" type="button" [class.btn-danger]="a === 'Cancel' || a === 'Suspend'" (click)="openAction(a)">{{ 'subs.action.' + a | t }}</button>
                }
              }
              @if (auth.can(auth.perm.LicensesManage) && (s.status === 'Active' || s.status === 'Trial')) {
                <button class="btn btn-primary" type="button" (click)="issue()">{{ 'subs.issueLicense' | t }}</button>
              }
            </div>
          }
        </header>

        @if (issued(); as i) {
          <app-secret [title]="'lic.keyOnce' | t" [hint]="'lic.keyHint' | t" [value]="i.productKey" />
        }

        <div class="grid-2">
          <section class="card">
            <h2>{{ 'common.details' | t }}</h2>
            <dl class="dl">
              <dt>{{ 'subs.start' | t }}</dt><dd>{{ s.startDate | date2 }}</dd>
              <dt>{{ 'subs.end' | t }}</dt><dd>{{ s.isLifetime ? ('common.lifetime' | t) : (s.endDate | date2) }}</dd>
              @if (s.trialEndsAt) { <dt>{{ 'subs.trialEnds' | t }}</dt><dd>{{ s.trialEndsAt | date2 }}</dd> }
              <dt>{{ 'common.version' | t }}</dt><dd>{{ s.version }}</dd>
              <dt>{{ 'common.createdAt' | t }}</dt><dd>{{ s.createdAt | date2: true }}</dd>
            </dl>
          </section>
          <section class="card">
            <h2>{{ 'subs.history' | t }}</h2>
            <ol class="timeline">
              @for (h of d.sub.history; track $index) {
                <li>
                  <strong>{{ 'subs.action.' + h.action | t }}</strong>
                  @if (h.fromStatus) { <span class="muted">{{ 'status.' + h.fromStatus | t }} → </span> }
                  <app-status [value]="h.toStatus" />
                  <br /><small class="muted">{{ h.at | date2: true }} @if (h.details) { · {{ h.details }} }</small>
                </li>
              }
            </ol>
          </section>
        </div>

        <section class="card">
          <h2>{{ 'subs.licenses' | t }}</h2>
          <div class="table-wrap">
            <table>
              <thead><tr><th>{{ 'lic.number' | t }}</th><th>{{ 'lic.prefix' | t }}</th><th>{{ 'common.status' | t }}</th><th>{{ 'lic.devices' | t }}</th><th>{{ 'lic.expires' | t }}</th></tr></thead>
              <tbody>
                @for (l of d.licenses.items; track l.id) {
                  <tr><td><a [routerLink]="['/licenses', l.id]" dir="ltr">{{ l.licenseNumber }}</a></td>
                    <td dir="ltr"><code>{{ l.productKeyPrefix }}-••••</code></td>
                    <td><app-status [value]="l.status" /></td>
                    <td>{{ l.activeActivations | num }} / {{ l.maxActivations ?? '∞' }}</td>
                    <td>{{ l.expiresAt ? (l.expiresAt | date2) : ('common.lifetime' | t) }}</td></tr>
                } @empty { <tr><td colspan="5" class="muted">{{ 'common.empty' | t }}</td></tr> }
              </tbody>
            </table>
          </div>
        </section>
      }
    </app-state>

    <app-modal [(open)]="actionOpen" [title]="('subs.action.' + action()) | t">
      <form class="form" (ngSubmit)="runAction()">
        @if (action() === 'ChangePlan') {
          <label class="field"><span>{{ 'subs.newPlan' | t }}</span>
            <select name="plan" required [(ngModel)]="planId">
              @for (p of plans(); track p.id) { <option [value]="p.id">{{ p.name }} v{{ p.version }} ({{ p.price }} {{ p.currency }})</option> }
            </select>
          </label>
        }
        @if (action() === 'Renew') {
          <label class="field"><span>{{ 'plans.duration' | t }} <small class="muted">({{ 'common.optional' | t }})</small></span>
            <input name="days" type="number" min="1" [(ngModel)]="days" /></label>
        }
        @if (action() === 'Suspend' || action() === 'Cancel') {
          <label class="field"><span>{{ 'common.reason' | t }}</span><input name="reason" [(ngModel)]="reason" /></label>
        }
        <div class="form-actions"><button class="btn btn-primary" type="submit">{{ 'subs.action.' + action() | t }}</button></div>
      </form>
    </app-modal>
  `,
})
export class SubscriptionDetailsPage implements OnInit {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private i18n = inject(I18n);
  readonly auth = inject(AuthService);
  readonly id = input.required<string>();

  readonly data = loader(() => forkJoin({
    sub: this.api.subscription(this.id()),
    licenses: this.api.licenses({ subscriptionId: this.id(), pageSize: 100 }),
  }), false);
  readonly issued = signal<IssuedLicense | null>(null);

  readonly actionOpen = signal(false);
  readonly action = signal<SubscriptionAction>('Renew');
  readonly plans = signal<Plan[]>([]);
  reason = '';
  days: number | null = null;
  planId = '';

  ngOnInit() { this.data.load(); }

  openAction(a: SubscriptionAction) {
    this.action.set(a);
    this.reason = ''; this.days = null; this.planId = '';
    if (a === 'ChangePlan') {
      const productId = this.data.data()!.sub.subscription.productId;
      this.api.plans({ productId, status: 'Published', pageSize: 100 }).subscribe(r => this.plans.set(r.items));
    }
    this.actionOpen.set(true);
  }

  runAction() {
    const version = this.data.data()!.sub.subscription.version;
    const a = this.action();
    const req = a === 'Renew' ? this.api.subscriptionAction(this.id(), 'renew', { durationDays: this.days || null, expectedVersion: version })
      : a === 'ChangePlan' ? this.api.subscriptionAction(this.id(), 'change-plan', { planId: this.planId, expectedVersion: version })
      : this.api.subscriptionAction(this.id(), a.toLowerCase() as 'suspend' | 'resume' | 'cancel', { reason: this.reason || null, expectedVersion: version });
    req.subscribe(() => { this.toasts.success(this.i18n.t('common.saved')); this.actionOpen.set(false); this.data.load(); });
  }

  issue() {
    this.api.issueLicense(this.id()).subscribe(r => { this.issued.set(r); this.data.load(); });
  }
}
