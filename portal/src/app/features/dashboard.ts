import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Kpi } from '../core/api.models';
import { AgoPipe, I18n, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { CHART_COLORS, Donut, LineChart, Series } from '../shared/charts';
import { Icon } from '../shared/icon';
import { StateView, loader } from '../shared/ui';

/** Dashboard from the mockup: five KPI tiles, revenue/subscriptions trend, licenses by product, recent activations, needs attention. */
@Component({
  selector: 'app-dashboard',
  imports: [FormsModule, RouterLink, TranslatePipe, LocalNumberPipe, AgoPipe, StateView, Icon, LineChart, Donut],
  template: `
    <header class="page-head">
      <div>
        <h1>{{ 'dash.title' | t }}</h1>
        <p class="subtitle">{{ 'dash.welcome' | t: { name: firstName() } }}</p>
      </div>
      <span class="date-range"><app-icon name="calendar" [size]="16" />{{ monthRange() }}</span>
    </header>

    <app-state [loading]="data.loading() && !o()" [error]="data.error()" (retry)="data.load()">
      @if (o(); as o) {
        <section class="kpi-row">
          @if (auth.isPlatformAdmin()) {
            <a class="kpi2" routerLink="/tenants">
              <span class="tile blue"><app-icon name="building" [size]="22" /></span>
              <div><span class="label">{{ 'k.totalTenants' | t }}</span><span class="value">{{ o.tenants.value | num }}</span>
                <span class="delta"><b [class.down]="(o.tenants.changePercent ?? 0) < 0">{{ pct(o.tenants) }}</b> {{ 'k.fromLastMonth' | t }}</span></div>
            </a>
          } @else if (!auth.isCustomerUser()) {
            <a class="kpi2" routerLink="/customers">
              <span class="tile blue"><app-icon name="users" [size]="22" /></span>
              <div><span class="label">{{ 'k.customers' | t }}</span><span class="value">{{ o.customers.value | num }}</span>
                <span class="delta"><b [class.down]="(o.customers.changePercent ?? 0) < 0">{{ pct(o.customers) }}</b> {{ 'k.fromLastMonth' | t }}</span></div>
            </a>
          }
          <a class="kpi2" routerLink="/licenses">
            <span class="tile green"><app-icon name="key" [size]="22" /></span>
            <div><span class="label">{{ 'k.activeLicenses' | t }}</span><span class="value">{{ o.activeLicenses.value | num }}</span>
              <span class="delta"><b [class.down]="(o.activeLicenses.changePercent ?? 0) < 0">{{ pct(o.activeLicenses) }}</b> {{ 'k.fromLastMonth' | t }}</span></div>
          </a>
          <a class="kpi2" routerLink="/subscriptions">
            <span class="tile violet"><app-icon name="list" [size]="22" /></span>
            <div><span class="label">{{ 'k.subscriptions' | t }}</span><span class="value">{{ o.subscriptions.value | num }}</span>
              <span class="delta"><b [class.down]="(o.subscriptions.changePercent ?? 0) < 0">{{ pct(o.subscriptions) }}</b> {{ 'k.fromLastMonth' | t }}</span></div>
          </a>
          <a class="kpi2" routerLink="/subscriptions">
            <span class="tile orange"><app-icon name="invoice" [size]="22" /></span>
            <div><span class="label">{{ 'k.revenue' | t }}</span><span class="value" dir="ltr">{{ i18n.money(o.revenue.value, o.currency) }}</span>
              <span class="delta"><b [class.down]="(o.revenue.changePercent ?? 0) < 0">{{ pct(o.revenue) }}</b> {{ 'k.fromLastMonth' | t }}</span></div>
          </a>
          <a class="kpi2" routerLink="/reports">
            <span class="tile red"><app-icon name="alert" [size]="22" /></span>
            <div><span class="label">{{ 'k.expiringSoon' | t }}</span><span class="value">{{ o.expiringSoon | num }}</span>
              <span class="delta">{{ 'k.next30' | t }}</span></div>
          </a>
        </section>

        <section class="charts">
          <div class="card">
            <div class="card-head">
              <h2>{{ 'chart.trend' | t }}</h2>
              <div class="chart-tools">
                <div class="seg">
                  <button type="button" [class.active]="granularity() === 'monthly'" (click)="granularity.set('monthly')">{{ 'chart.monthly' | t }}</button>
                  <button type="button" [class.active]="granularity() === 'yearly'" (click)="granularity.set('yearly')">{{ 'chart.yearly' | t }}</button>
                </div>
                @if (granularity() === 'monthly') {
                  <select [ngModel]="year()" (ngModelChange)="year.set(+$event)" aria-label="year">
                    @for (y of years; track y) { <option [ngValue]="y">{{ y }}</option> }
                  </select>
                }
              </div>
            </div>
            <div class="chart-legend">
              <span><i style="background:#2563eb"></i>{{ 'chart.revenue' | t }}</span>
              <span><i style="background:#10b981"></i>{{ 'chart.subscriptions' | t }}</span>
            </div>
            @if (trend.data(); as points) {
              <app-line-chart [series]="series()" [labels]="labels()" [label]="'chart.trend' | t" [format]="axisFormat" />
            }
          </div>

          <div class="card">
            <div class="card-head"><h2>{{ 'chart.byProduct' | t }}</h2></div>
            <div class="donut-wrap">
              <app-donut [slices]="slices()" [centerValue]="(o.totalLicenses | num)" [centerLabel]="'chart.totalLicenses' | t" />
              <ul class="legend-list">
                @for (s of o.licensesByProduct; track s.productCode; let i = $index) {
                  <li><i [style.background]="color(i)"></i><span>{{ s.productName }}</span><b>{{ s.licenses | num }}</b><span class="pct">{{ s.percent }}%</span></li>
                }
              </ul>
            </div>
          </div>
        </section>

        <section class="lists">
          <div class="card">
            <div class="card-head">
              <h2>{{ 'dash.recentActivations' | t }}</h2>
              <a class="view-all" routerLink="/activations">{{ 'dash.viewAll' | t }} <app-icon name="arrowRight" class="flip" [size]="14" /></a>
            </div>
            <div class="table-wrap">
              <table>
                <thead><tr><th>{{ 'col.customer' | t }}</th><th>{{ 'col.product' | t }}</th><th>{{ 'col.device' | t }}</th><th>{{ 'col.ip' | t }}</th><th>{{ 'col.date' | t }}</th><th></th></tr></thead>
                <tbody>
                  @for (a of o.recentActivations; track $index) {
                    <tr class="clickable" [routerLink]="['/licenses', a.licenseId]">
                      <td><span class="status-dot" [class.ok]="a.online"></span>{{ a.customerName }}</td>
                      <td>{{ a.productName }}</td>
                      <td dir="ltr">{{ a.deviceName ?? a.deviceId }}</td>
                      <td dir="ltr">{{ a.ipAddress ?? '—' }}</td>
                      <td>{{ a.at | ago }}</td>
                      <td><app-icon name="chevronRight" class="flip muted" [size]="16" /></td>
                    </tr>
                  } @empty { <tr><td colspan="6" class="muted">{{ 'common.empty' | t }}</td></tr> }
                </tbody>
              </table>
            </div>
          </div>

          <div class="card">
            <div class="card-head"><h2>{{ 'dash.needsAttention' | t }}</h2></div>
            <ul class="attention">
              <li><span class="tile sm red"><app-icon name="alert" /></span><div><strong>{{ o.attention.expiredLicenses | num }}</strong><small>{{ 'att.expired' | t }}</small></div>
                <a routerLink="/licenses" [queryParams]="{ status: 'Expired' }">{{ 'att.view' | t }} <app-icon name="arrowRight" class="flip" [size]="14" /></a></li>
              <li><span class="tile sm orange"><app-icon name="invoice" /></span><div><strong>{{ o.attention.renewalsDue | num }}</strong><small>{{ 'att.renewals' | t }}</small></div>
                <a routerLink="/reports">{{ 'att.view' | t }} <app-icon name="arrowRight" class="flip" [size]="14" /></a></li>
              <li><span class="tile sm orange"><app-icon name="bell" /></span><div><strong>{{ o.attention.limitReached | num }}</strong><small>{{ 'att.limit' | t }}</small></div>
                <a routerLink="/licenses">{{ 'att.view' | t }} <app-icon name="arrowRight" class="flip" [size]="14" /></a></li>
              <li><span class="tile sm teal"><app-icon name="shieldAlert" /></span><div><strong>{{ o.attention.suspiciousActivations | num }}</strong><small>{{ 'att.suspicious' | t }}</small></div>
                <a routerLink="/reports" [queryParams]="{ tab: 'failed' }">{{ 'att.view' | t }} <app-icon name="arrowRight" class="flip" [size]="14" /></a></li>
            </ul>
          </div>
        </section>
      }
    </app-state>
  `,
})
export class DashboardPage {
  private api = inject(Api);
  readonly auth = inject(AuthService);
  readonly i18n = inject(I18n);

  readonly years = Array.from({ length: 5 }, (_, i) => new Date().getFullYear() - i);
  readonly year = signal(new Date().getFullYear());
  readonly granularity = signal<'monthly' | 'yearly'>('monthly');

  readonly data = loader(() => this.api.overview());
  readonly trend = loader(() => this.api.trend(this.granularity(), this.year()), false);
  readonly o = computed(() => this.data.data());

  constructor() {
    effect(() => { this.granularity(); this.year(); this.trend.load(); });
  }

  /** Current month, as the date range shown in the header. */
  readonly monthRange = computed(() => {
    const now = new Date();
    const first = new Date(now.getFullYear(), now.getMonth(), 1).toISOString();
    const last = new Date(now.getFullYear(), now.getMonth() + 1, 0).toISOString();
    return `${this.i18n.date(first)} - ${this.i18n.date(last)}`;
  });

  readonly firstName = computed(() => (this.auth.user()?.fullName ?? '').split(' ')[0]);

  readonly labels = computed(() => (this.trend.data() ?? []).map(p => this.granularity() === 'monthly' ? this.i18n.t('m.' + p.label) : p.label));
  readonly series = computed<Series[]>(() => {
    const pts = this.trend.data() ?? [];
    return [
      { name: this.i18n.t('chart.revenue'), color: '#2563eb', values: pts.map(p => p.revenue) },
      { name: this.i18n.t('chart.subscriptions'), color: '#10b981', values: pts.map(p => p.subscriptions) },
    ];
  });
  readonly slices = computed(() => (this.o()?.licensesByProduct ?? []).map((s, i) => ({ label: s.productName, value: s.licenses, color: this.color(i) })));

  readonly axisFormat = (v: number) => v >= 1000 ? `${Math.round(v / 100) / 10}K` : String(Math.round(v));

  color(i: number) { return CHART_COLORS[i % CHART_COLORS.length]; }

  pct(k: Kpi): string {
    if (k.changePercent === null) return '+100%';
    if (k.changePercent === 0) return this.i18n.t('k.noChange');
    return `${k.changePercent > 0 ? '+' : ''}${k.changePercent}%`;
  }
}
