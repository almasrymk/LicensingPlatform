import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Activation } from '../core/api.models';
import { I18n, LocalDatePipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Modal, PageHead, StateView, StatusBadge, loader } from '../shared/ui';
import { Icon } from '../shared/icon';

@Component({
  selector: 'app-license-details',
  imports: [FormsModule, RouterLink, TranslatePipe, LocalDatePipe, StateView, StatusBadge, Modal, PageHead, Icon],
  template: `
    <app-state [loading]="data.loading() && !data.data()" [error]="data.error()" (retry)="data.load()">
      @if (data.data(); as d) {
        @let l = d.license;
        <app-page-head title="lic.detailTitle" en="License &amp; Device Activations" subtitle="lic.detailSub" back="/licenses">
          @if (auth.can(auth.perm.LicensesManage)) {
            @if (l.status === 'Active') {
              <button class="btn" type="button" (click)="openReason('suspend')">{{ 'lic.suspend' | t }}</button>
            }
            @if (l.status === 'Suspended') {
              <button class="btn btn-primary" type="button" (click)="act('resume')"><app-icon name="refresh" [size]="16" />{{ 'lic.resume' | t }}</button>
            }
            @if (l.status !== 'Revoked') {
              <button class="btn btn-danger" type="button" (click)="openReason('revoke')"><app-icon name="ban" [size]="16" />{{ 'lic.revoke' | t }} (Revoke)</button>
            }
          }
        </app-page-head>

        <section class="summary">
          <div><small>{{ 'lic.number' | t }}</small><strong class="mono" dir="ltr">{{ l.licenseNumber }}</strong></div>
          <div><small>{{ 'lic.customerLabel' | t }}</small><strong><a [routerLink]="['/customers', l.customerId]">{{ l.customerName }}</a></strong></div>
          <div><small>{{ 'lic.devicesUsed' | t }}</small>
            <strong class="accent">{{ l.maxActivations ? ('lic.devicesOf' | t: { used: l.activeActivations, max: l.maxActivations }) : (l.activeActivations + ' / ∞') }}</strong>
            <div class="meter big" [class.full]="l.maxActivations && l.activeActivations >= l.maxActivations"><span [style.width.%]="l.maxActivations ? (l.activeActivations / l.maxActivations) * 100 : 10"></span></div>
          </div>
          <div><small>{{ 'lic.statusLabel' | t }}</small><app-status [value]="l.status" />
            @if (l.statusReason) { <span class="cell-sub">{{ l.statusReason }}</span> }</div>
        </section>

        <div class="grid-2">
          <section class="card">
            <h2>{{ 'plans.entitlements' | t }}</h2>
            <dl class="dl">
              <dt>{{ 'common.product' | t }}</dt><dd><a [routerLink]="['/subscriptions', l.subscriptionId]">{{ l.productCode }} / {{ l.planCode }} v{{ l.planVersion }}</a></dd>
              <dt>{{ 'lic.prefix' | t }}</dt><dd dir="ltr"><code>{{ l.productKeyPrefix }}-••••••-••••••-••••••-••••••</code></dd>
              <dt>{{ 'lic.issued' | t }}</dt><dd>{{ l.issuedAt | date2: true }}</dd>
              <dt>{{ 'lic.expires' | t }}</dt><dd>{{ l.expiresAt ? (l.expiresAt | date2) : ('common.lifetime' | t) }}</dd>
            </dl>
            <div class="chips" style="margin-top:12px">@for (f of l.features; track f) { <span class="chip" dir="ltr">{{ f }}</span> }</div>
          </section>
          <section class="card">
            <h2>{{ 'settings.title' | t }}</h2>
            <div class="settings-list">
              <div><span>{{ 'lic.heartbeatEvery' | t }} (Heartbeat Interval)</span><strong>{{ 'common.hours' | t: { n: l.heartbeatIntervalHours } }}</strong></div>
              <div><span>{{ 'lic.offlineGrace' | t }} (Grace Period)</span><strong>{{ 'common.days' | t: { n: l.offlineGraceDays } }}</strong></div>
              <div><span>{{ 'plans.maxActivations' | t }}</span><strong>{{ l.maxActivations ?? ('common.unlimited' | t) }}</strong></div>
            </div>
          </section>
        </div>

        <section class="card">
          <h2>{{ 'lic.registered' | t }} @if (i18n.lang() === 'ar') { <span dir="ltr">(Registered Devices / Instances)</span> }</h2>
          <div class="table-wrap">
            <table>
              <thead><tr>
                <th>{{ 'lic.deviceId' | t }} (Fingerprint / ID)</th><th>{{ 'lic.appVersion' | t }}</th><th>{{ 'lic.ip' | t }}</th>
                <th>{{ 'lic.lastSeen' | t }} (Last Seen)</th><th>{{ 'common.status' | t }}</th><th>{{ 'common.actions' | t }}</th>
              </tr></thead>
              <tbody>
                @for (a of d.activations; track a.id) {
                  <tr [class.dim]="a.status !== 'Active'">
                    <td><span class="mono" dir="ltr">{{ a.deviceId }}</span>@if (a.deviceName) { <span class="cell-sub">{{ a.deviceName }}</span> }</td>
                    <td dir="ltr">{{ a.appVersion ?? '—' }}</td>
                    <td dir="ltr">{{ a.lastIpAddress ?? '—' }}</td>
                    <td>{{ a.lastHeartbeatAt | date2: true }}</td>
                    <td>
                      @if (a.status !== 'Active') { <app-status [value]="a.status" /> }
                      @else if (isOnline(a)) { <span class="badge ok">{{ 'lic.online' | t }} (Online)</span> }
                      @else { <span class="badge neutral">{{ 'lic.offline' | t }} (Offline)</span> }
                    </td>
                    <td>
                      @if (a.status === 'Active' && auth.can(auth.perm.LicensesManage)) {
                        <button class="btn btn-sm" type="button" (click)="reset(a)">{{ 'lic.reset' | t }}</button>
                      }
                    </td>
                  </tr>
                } @empty { <tr><td colspan="6" class="muted">{{ 'common.empty' | t }}</td></tr> }
              </tbody>
            </table>
          </div>
        </section>
      }
    </app-state>

    <app-modal [(open)]="reasonOpen" [title]="(pending() === 'revoke' ? 'lic.revoke' : 'lic.suspend') | t">
      <form class="form" (ngSubmit)="act(pending())">
        @if (pending() === 'revoke') { <p class="hint">{{ 'lic.revokeHint' | t }}</p> }
        <label class="field"><span>{{ 'common.reason' | t }}</span><input name="reason" [(ngModel)]="reason" /></label>
        <div class="form-actions">
          <button class="btn btn-danger" type="submit">{{ (pending() === 'revoke' ? 'lic.revoke' : 'lic.suspend') | t }}</button>
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
  readonly data = loader(() => this.api.license(this.id()), false);

  readonly reasonOpen = signal(false);
  readonly pending = signal<'suspend' | 'revoke' | 'resume'>('suspend');
  reason = '';

  ngOnInit() { this.data.load(); }

  /** Online when the device checked in within two heartbeat intervals. */
  isOnline(a: Activation): boolean {
    const hours = this.data.data()?.license.heartbeatIntervalHours ?? 24;
    return !!a.lastHeartbeatAt && Date.now() - new Date(a.lastHeartbeatAt).getTime() < hours * 2 * 3600_000;
  }

  openReason(action: 'suspend' | 'revoke') { this.pending.set(action); this.reason = ''; this.reasonOpen.set(true); }

  act(action: 'suspend' | 'resume' | 'revoke') {
    this.api.licenseAction(this.id(), action, this.reason || undefined).subscribe(r => {
      this.toasts.success(this.i18n.t('common.saved'));
      this.reasonOpen.set(false);
      this.data.data.set(r);
    });
  }

  reset(a: Activation) {
    if (!confirm(this.i18n.t('common.confirm'))) return;
    this.api.resetDevice(this.id(), a.id).subscribe(r => { this.toasts.success(this.i18n.t('common.saved')); this.data.data.set(r); });
  }
}
