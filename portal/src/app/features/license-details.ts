import { Component, OnInit, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Activation, IssuedLicense } from '../core/api.models';
import { AgoPipe, I18n, LocalDatePipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Icon } from '../shared/icon';
import { Modal, SecretBox, StateView, StatusBadge, loader } from '../shared/ui';

type Tab = 'overview' | 'activations' | 'features' | 'history' | 'security';

/** License details from the mockup: actions bar, tabs, summary cards and the product key area. */
@Component({
  selector: 'app-license-details',
  imports: [FormsModule, RouterLink, TranslatePipe, LocalDatePipe, AgoPipe, StateView, StatusBadge, Modal, SecretBox, Icon],
  template: `
    <app-state [loading]="data.loading() && !data.data()" [error]="data.error()" (retry)="data.load()">
      @if (data.data(); as d) {
        @let l = d.license;
        <nav class="crumbs"><a routerLink="/licenses">{{ 'nav.licenses' | t }}</a><span>/</span><span class="mono" dir="ltr">{{ l.licenseNumber }}</span></nav>
        <header class="page-head">
          <div>
            <h1><span class="mono" dir="ltr">{{ l.licenseNumber }}</span> <app-status [value]="l.status" /></h1>
            <p class="subtitle">{{ l.productCode }} - {{ l.planCode }} v{{ l.planVersion }}</p>
          </div>
          @if (auth.can(auth.perm.LicensesManage)) {
            <div class="actions">
              @if (auth.can(auth.perm.SubscriptionsManage) && l.status !== 'Revoked') {
                <button class="btn btn-primary" type="button" (click)="renew()"><app-icon name="refresh" [size]="16" />{{ 'lic.renew' | t }}</button>
              }
              @if (l.status === 'Active') {
                <button class="btn btn-warn" type="button" (click)="openReason('suspend')">{{ 'lic.suspend' | t }}</button>
              }
              @if (l.status === 'Suspended') {
                <button class="btn" type="button" (click)="act('resume')">{{ 'lic.resume' | t }}</button>
              }
              @if (l.status !== 'Revoked') {
                <button class="btn btn-solid-danger" type="button" (click)="openReason('revoke')">{{ 'lic.revoke' | t }}</button>
              }
            </div>
          }
        </header>

        @if (newKey(); as k) { <app-secret [title]="'lic.keyOnce' | t" [hint]="'lic.keyHint' | t" [value]="k.productKey" /> }

        <div class="tabs" role="tablist">
          @for (t of tabs; track t.id) {
            <button type="button" role="tab" [class.active]="tab() === t.id" [attr.aria-selected]="tab() === t.id" (click)="tab.set(t.id)">{{ t.label | t }}</button>
          }
        </div>

        @switch (tab()) {
          @case ('overview') {
            <section class="sum-cards">
              <div><small>{{ 'col.customer' | t }}</small><strong>{{ l.customerName }}</strong>
                <a class="sub" [routerLink]="['/customers', l.customerId]">{{ 'common.view' | t }}</a></div>
              <div><small>{{ 'col.expiry' | t }}</small><strong>{{ l.expiresAt ? (l.expiresAt | date2) : ('common.lifetime' | t) }}</strong>
                @if (daysLeft() !== null) { <span class="sub">{{ daysLeft()! > 0 ? ('lic.inDays' | t: { n: daysLeft()! }) : ('lic.expired' | t) }}</span> }</div>
              <div><small>{{ 'lic.devicesUsedShort' | t }}</small><strong dir="ltr">{{ l.activeActivations }} / {{ l.maxActivations ?? '∞' }}</strong>
                <div class="bar"><span [style.width.%]="devicePct()"></span></div>
                <span class="sub">{{ 'lic.ofLimit' | t: { pct: devicePct() } }}</span></div>
              <div><small>{{ 'lic.successfulActivations' | t }}</small><strong dir="ltr">{{ d.activations.length }} / {{ l.maxActivations ?? '∞' }}</strong>
                <div class="bar"><span [style.width.%]="activationPct()"></span></div>
                <span class="sub">{{ 'lic.ofLimit' | t: { pct: activationPct() } }}</span></div>
            </section>

            <section class="card">
              <h2>{{ 'lic.productKey' | t }}</h2>
              <div class="key-box">
                <code dir="ltr">{{ l.productKeyPrefix }} - •••••• - •••••• - •••••• - ••••••</code>
                <button class="btn" type="button" disabled [title]="'lic.revealHint' | t"><app-icon name="eye" [size]="16" />{{ 'lic.reveal' | t }}</button>
                <button class="btn" type="button" (click)="copy(l.productKeyPrefix)"><app-icon name="copy" [size]="16" />{{ 'common.copy' | t }}</button>
                @if (auth.can(auth.perm.LicensesManage) && l.status !== 'Revoked') {
                  <button class="btn btn-primary" type="button" (click)="regenerate()"><app-icon name="refresh" [size]="16" />{{ 'lic.regenerate' | t }}</button>
                }
              </div>
              <p class="muted" style="margin:10px 0 0">{{ 'lic.revealHint' | t }}</p>
            </section>
          }
          @case ('activations') {
            <div class="table-card"><div class="table-wrap"><table>
              <thead><tr><th>{{ 'col.device' | t }}</th><th>{{ 'col.os' | t }}</th><th>{{ 'col.ip' | t }}</th><th>{{ 'col.activated' | t }}</th><th>{{ 'col.lastSeen' | t }}</th><th>{{ 'col.status' | t }}</th><th></th></tr></thead>
              <tbody>
                @for (a of d.activations; track a.id) {
                  <tr>
                    <td><strong class="mono" dir="ltr">{{ a.deviceId }}</strong>@if (a.deviceName) { <span class="cell-sub">{{ a.deviceName }}</span> }</td>
                    <td>{{ a.operatingSystem ?? '—' }}</td>
                    <td dir="ltr">{{ a.lastIpAddress ?? '—' }}</td>
                    <td>{{ a.activatedAt | date2 }}</td>
                    <td>{{ a.lastHeartbeatAt | ago }}</td>
                    <td>
                      @if (a.status !== 'Active') { <span class="disabled-st"><span class="status-dot bad"></span>{{ 'act.disabled' | t }}</span> }
                      @else if (a.online) { <span class="online"><span class="status-dot ok"></span>{{ 'act.online' | t }}</span> }
                      @else { <span class="offline"><span class="status-dot"></span>{{ 'act.offline' | t }}</span> }
                    </td>
                    <td>@if (a.status === 'Active' && auth.can(auth.perm.LicensesManage)) { <button class="btn btn-sm" type="button" (click)="reset(a)">{{ 'lic.reset' | t }}</button> }</td>
                  </tr>
                } @empty { <tr><td colspan="7" class="muted">{{ 'common.empty' | t }}</td></tr> }
              </tbody>
            </table></div></div>
          }
          @case ('features') {
            <div class="card">
              <h2>{{ 'plans.entitlements' | t }}</h2>
              <ul class="plain">
                @for (f of l.features; track f) { <li><app-icon name="checkCircle" class="c-green" [size]="16" /> <span dir="ltr">{{ f }}</span></li> }
                @empty { <li class="muted">{{ 'common.empty' | t }}</li> }
              </ul>
            </div>
          }
          @case ('history') {
            <div class="table-card"><div class="table-wrap"><table>
              <thead><tr><th>{{ 'audit.at' | t }}</th><th>{{ 'audit.action' | t }}</th><th>{{ 'audit.actor' | t }}</th><th>{{ 'common.details' | t }}</th></tr></thead>
              <tbody>
                @for (h of history.data()?.items; track h.id) {
                  <tr><td>{{ h.at | date2: true }}</td><td class="mono" dir="ltr">{{ h.action }}</td><td dir="ltr">{{ h.actorName ?? h.actorType }}</td><td><small>{{ h.details }}</small></td></tr>
                } @empty { <tr><td colspan="4" class="muted">{{ 'common.empty' | t }}</td></tr> }
              </tbody>
            </table></div></div>
          }
          @case ('security') {
            <div class="card">
              <div class="settings-list">
                <div><span>{{ 'lic.heartbeatEvery' | t }} (Heartbeat Interval)</span><strong>{{ 'common.hours' | t: { n: l.heartbeatIntervalHours } }}</strong></div>
                <div><span>{{ 'lic.offlineGrace' | t }} (Grace Period)</span><strong>{{ 'common.days' | t: { n: l.offlineGraceDays } }}</strong></div>
                <div><span>{{ 'plans.maxActivations' | t }}</span><strong>{{ l.maxActivations ?? ('common.unlimited' | t) }}</strong></div>
                <div><span>{{ 'lic.prefix' | t }}</span><strong class="mono" dir="ltr">{{ l.productKeyPrefix }}</strong></div>
                <div><span>{{ 'lic.issued' | t }}</span><strong>{{ l.issuedAt | date2: true }}</strong></div>
              </div>
            </div>
          }
        }
      }
    </app-state>

    <app-modal [(open)]="reasonOpen" [title]="(pending() === 'revoke' ? 'lic.revoke' : 'lic.suspend') | t">
      <form class="form" (ngSubmit)="act(pending())">
        @if (pending() === 'revoke') { <p class="hint">{{ 'lic.revokeHint' | t }}</p> }
        <label class="field"><span>{{ 'common.reason' | t }}</span><input name="reason" [(ngModel)]="reason" /></label>
        <div class="form-actions">
          <button class="btn btn-solid-danger" type="submit">{{ (pending() === 'revoke' ? 'lic.revoke' : 'lic.suspend') | t }}</button>
        </div>
      </form>
    </app-modal>
  `,
})
export class LicenseDetailsPage implements OnInit {
  private api = inject(Api);
  private toasts = inject(Toasts);
  readonly i18n = inject(I18n);
  readonly auth = inject(AuthService);
  readonly id = input.required<string>();

  readonly tabs: { id: Tab; label: string }[] = [
    { id: 'overview', label: 'lic.overviewTab' }, { id: 'activations', label: 'lic.activationsTab' }, { id: 'features', label: 'lic.featuresTab' },
    { id: 'history', label: 'lic.historyTab' }, { id: 'security', label: 'lic.securityTab' },
  ];
  readonly tab = signal<Tab>('overview');

  readonly data = loader(() => this.api.license(this.id()), false);
  readonly history = loader(() => this.api.audit({ entityId: this.id(), pageSize: 100 }), false);
  readonly newKey = signal<IssuedLicense | null>(null);

  readonly reasonOpen = signal(false);
  readonly pending = signal<'suspend' | 'revoke' | 'resume'>('suspend');
  reason = '';

  constructor() {
    effect(() => { if (this.tab() === 'history' && this.auth.can(this.auth.perm.AuditRead)) this.history.load(); });
  }

  ngOnInit() { this.data.load(); }

  readonly daysLeft = computed(() => {
    const exp = this.data.data()?.license.expiresAt;
    return exp ? Math.ceil((new Date(exp).getTime() - Date.now()) / 86400000) : null;
  });
  readonly devicePct = computed(() => {
    const l = this.data.data()?.license;
    return l?.maxActivations ? Math.round((l.activeActivations / l.maxActivations) * 100) : 0;
  });
  readonly activationPct = computed(() => {
    const d = this.data.data();
    return d?.license.maxActivations ? Math.min(100, Math.round((d.activations.length / d.license.maxActivations) * 100)) : 0;
  });

  private ok() { this.toasts.success(this.i18n.t('common.saved')); }

  copy(text: string) { navigator.clipboard?.writeText(text).then(() => this.toasts.info(this.i18n.t('common.copied')), () => undefined); }

  openReason(action: 'suspend' | 'revoke') { this.pending.set(action); this.reason = ''; this.reasonOpen.set(true); }

  act(action: 'suspend' | 'resume' | 'revoke') {
    this.api.licenseAction(this.id(), action, this.reason || undefined).subscribe(r => { this.ok(); this.reasonOpen.set(false); this.data.data.set(r); });
  }

  renew() {
    const l = this.data.data()?.license;
    if (!l) return;
    this.api.subscriptionAction(l.subscriptionId, 'renew', {}).subscribe(() => { this.ok(); this.data.load(); });
  }

  regenerate() {
    if (!confirm(this.i18n.t('lic.regenerateHint'))) return;
    this.api.regenerateKey(this.id()).subscribe(r => { this.newKey.set(r); this.data.load(); });
  }

  reset(a: Activation) {
    if (!confirm(this.i18n.t('common.confirm'))) return;
    this.api.resetDevice(this.id(), a.id).subscribe(r => { this.ok(); this.data.data.set(r); });
  }
}
