import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { LocalDatePipe, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { PageHead, StateView, loader } from '../shared/ui';

type Tab = 'expiring' | 'devices' | 'failed' | 'usage';

@Component({
  selector: 'app-reports',
  imports: [FormsModule, RouterLink, TranslatePipe, LocalDatePipe, LocalNumberPipe, StateView, PageHead],
  template: `
    <app-page-head title="reports.title" en="Reports" subtitle="reports.sub">
    </app-page-head>

    <div class="tabs" role="tablist">
      @for (t of tabs; track t.id) {
        <button type="button" role="tab" [attr.aria-selected]="tab() === t.id" [class.active]="tab() === t.id" (click)="tab.set(t.id)">{{ t.label | t }}</button>
      }
    </div>

    @switch (tab()) {
      @case ('expiring') {
        <div class="toolbar">
          <label class="inline">{{ 'reports.within' | t }}
            <select [ngModel]="days()" (ngModelChange)="days.set(+$event)">
              <option [ngValue]="7">{{ 'common.days' | t: { n: 7 } }}</option>
              <option [ngValue]="30">{{ 'common.days' | t: { n: 30 } }}</option>
              <option [ngValue]="90">{{ 'common.days' | t: { n: 90 } }}</option>
            </select>
          </label>
        </div>
        <app-state [loading]="expiring.loading()" [error]="expiring.error()" [empty]="(expiring.data() ?? []).length === 0" (retry)="expiring.load()">
          <div class="table-wrap"><table>
            <thead><tr><th>{{ 'lic.number' | t }}</th><th>{{ 'common.customer' | t }}</th><th>{{ 'common.product' | t }}</th><th>{{ 'lic.expires' | t }}</th><th>{{ 'reports.daysLeft' | t }}</th><th>{{ 'lic.devices' | t }}</th></tr></thead>
            <tbody>
              @for (l of expiring.data(); track l.licenseId) {
                <tr><td><a [routerLink]="['/licenses', l.licenseId]" dir="ltr">{{ l.licenseNumber }}</a></td><td>{{ l.customerName }}</td>
                  <td>{{ l.productCode }} / {{ l.planCode }}</td><td>{{ l.expiresAt | date2 }}</td>
                  <td><span class="badge" [class.bad]="l.daysLeft <= 7" [class.warn]="l.daysLeft > 7">{{ l.daysLeft | num }}</span></td>
                  <td>{{ l.activeActivations | num }}</td></tr>
              }
            </tbody>
          </table></div>
        </app-state>
      }
      @case ('devices') {
        <app-state [loading]="devices.loading()" [error]="devices.error()" [empty]="(devices.data() ?? []).length === 0" (retry)="devices.load()">
          <div class="table-wrap"><table>
            <thead><tr><th>{{ 'lic.device' | t }}</th><th>{{ 'lic.deviceId' | t }}</th><th>{{ 'common.customer' | t }}</th><th>{{ 'lic.number' | t }}</th><th>{{ 'lic.appVersion' | t }}</th><th>{{ 'lic.lastHeartbeat' | t }}</th></tr></thead>
            <tbody>
              @for (d of devices.data(); track d.activationId) {
                <tr><td>{{ d.deviceName ?? '—' }}</td><td dir="ltr"><code>{{ d.deviceId }}</code></td><td>{{ d.customerName }}</td>
                  <td><a [routerLink]="['/licenses', d.licenseId]" dir="ltr">{{ d.licenseNumber }}</a></td><td dir="ltr">{{ d.appVersion }}</td>
                  <td>{{ d.lastHeartbeatAt | date2: true }} @if (d.stale) { <span class="badge warn">{{ 'lic.stale' | t }}</span> }</td></tr>
              }
            </tbody>
          </table></div>
        </app-state>
      }
      @case ('failed') {
        <div class="toolbar">
          <label class="inline">{{ 'reports.within' | t }}
            <select [ngModel]="days()" (ngModelChange)="days.set(+$event)">
              <option [ngValue]="7">{{ 'common.days' | t: { n: 7 } }}</option>
              <option [ngValue]="30">{{ 'common.days' | t: { n: 30 } }}</option>
            </select>
          </label>
        </div>
        <app-state [loading]="failed.loading()" [error]="failed.error()" [empty]="(failed.data()?.items ?? []).length === 0" (retry)="failed.load()">
          <section class="card">
            <h2>{{ 'reports.byCode' | t }}</h2>
            @for (s of failed.data()?.summary; track s.errorCode) {
              <div class="hbar">
                <span dir="ltr"><code>{{ s.errorCode }}</code></span>
                <div class="hbar-track"><div class="hbar-fill" [style.width.%]="(s.count / maxFailed()) * 100"></div></div>
                <strong>{{ s.count | num }}</strong>
              </div>
            }
          </section>
          <div class="table-wrap"><table>
            <thead><tr><th>{{ 'reports.at' | t }}</th><th>{{ 'reports.errorCode' | t }}</th><th>{{ 'common.customer' | t }}</th><th>{{ 'lic.number' | t }}</th><th>{{ 'lic.prefix' | t }}</th><th>{{ 'lic.deviceId' | t }}</th><th>{{ 'lic.ip' | t }}</th></tr></thead>
            <tbody>
              @for (f of failed.data()?.items; track $index) {
                <tr><td>{{ f.at | date2: true }}</td><td dir="ltr"><code>{{ f.errorCode }}</code></td><td>{{ f.customerName ?? '—' }}</td>
                  <td dir="ltr">{{ f.licenseNumber ?? '—' }}</td><td dir="ltr">{{ f.productKeyPrefix ?? '—' }}</td><td dir="ltr">{{ f.deviceId }}</td><td dir="ltr">{{ f.ipAddress }}</td></tr>
              }
            </tbody>
          </table></div>
        </app-state>
      }
      @case ('usage') {
        <app-state [loading]="usage.loading()" [error]="usage.error()" [empty]="(usage.data() ?? []).length === 0" (retry)="usage.load()">
          <div class="table-wrap"><table>
            <thead><tr><th>{{ 'reports.day' | t }}</th><th>{{ 'common.customer' | t }}</th><th>{{ 'dash.activeLicenses' | t }}</th><th>{{ 'dash.devices' | t }}</th><th>{{ 'dash.successful' | t }}</th><th>{{ 'dash.failed' | t }}</th></tr></thead>
            <tbody>
              @for (u of usage.data(); track $index) {
                <tr><td>{{ u.day | date2 }}</td><td>{{ u.customerName }}</td><td>{{ u.activeLicenses | num }}</td><td>{{ u.activeDevices | num }}</td>
                  <td>{{ u.successfulActivations | num }}</td><td>{{ u.failedActivations | num }}</td></tr>
              }
            </tbody>
          </table></div>
        </app-state>
      }
    }
  `,
})
export class ReportsPage {
  private api = inject(Api);
  private route = inject(ActivatedRoute);
  readonly tabs: { id: Tab; label: string }[] = [
    { id: 'expiring', label: 'reports.expiring' }, { id: 'devices', label: 'reports.devices' },
    { id: 'failed', label: 'reports.failed' }, { id: 'usage', label: 'reports.usage' },
  ];
  readonly tab = signal<Tab>((this.route.snapshot.queryParamMap.get('tab') as Tab) ?? 'expiring');
  readonly days = signal(30);

  readonly expiring = loader(() => this.api.expiring(this.days()), false);
  readonly devices = loader(() => this.api.activeDevices(), false);
  readonly failed = loader(() => this.api.failedActivations(this.days()), false);
  readonly usage = loader(() => this.api.usage(30), false);
  readonly maxFailed = computed(() => Math.max(1, ...(this.failed.data()?.summary ?? []).map(s => s.count)));

  constructor() {
    effect(() => {
      this.days();
      switch (this.tab()) {
        case 'expiring': this.expiring.load(); break;
        case 'devices': this.devices.load(); break;
        case 'failed': this.failed.load(); break;
        case 'usage': this.usage.load(); break;
      }
    });
  }
}
