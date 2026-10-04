import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { LocalDatePipe, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { Icon } from '../shared/icon';
import { PageHead, StateView, StatusBadge, loader } from '../shared/ui';

interface AlertItem { tone: 'bad' | 'warn' | 'info'; title: string; body: string; n: number; link: string; params?: Record<string, string>; }

/** Design page 2: four KPI cards, recently issued licenses, operational alerts. */
@Component({
  selector: 'app-dashboard',
  imports: [TranslatePipe, LocalNumberPipe, LocalDatePipe, StateView, StatusBadge, PageHead, Icon, RouterLink],
  template: `
    <app-page-head title="dash.title" en="Platform Dashboard" [subtitle]="auth.isCustomerUser() ? 'dash.customerSub' : 'dash.sub'">
      <a class="btn" routerLink="/reports"><app-icon name="filter" [size]="16" />{{ 'dash.filter' | t }}</a>
      @if (auth.can(auth.perm.TenantsManage)) {
        <a class="btn btn-primary" routerLink="/tenants" [queryParams]="{ new: 1 }"><app-icon name="plus" [size]="16" />{{ 'tenants.new' | t }}</a>
      } @else if (auth.can(auth.perm.CustomersManage)) {
        <a class="btn btn-primary" routerLink="/customers" [queryParams]="{ new: 1 }"><app-icon name="plus" [size]="16" />{{ 'customers.new' | t }}</a>
      }
    </app-page-head>

    <app-state [loading]="data.loading() && !d()" [error]="data.error()" (retry)="data.load()">
      @if (d(); as d) {
        <section class="kpis">
          @if (auth.isPlatformAdmin()) {
            <a class="kpi" routerLink="/tenants">
              <div class="kpi-top"><span>{{ 'dash.activeTenants' | t }}</span><app-icon name="building" class="c-blue" /></div>
              <strong>{{ d.activeTenants | num }}</strong>
              <small>{{ 'dash.ofTotal' | t: { n: d.tenants } }}</small>
            </a>
          } @else {
            <a class="kpi" [routerLink]="auth.isCustomerUser() ? '/reports' : '/customers'" [queryParams]="auth.isCustomerUser() ? { tab: 'devices' } : {}">
              <div class="kpi-top">
                <span>{{ (auth.isCustomerUser() ? 'dash.devices' : 'dash.customers') | t }}</span>
                <app-icon [name]="auth.isCustomerUser() ? 'monitor' : 'users'" class="c-blue" />
              </div>
              <strong>{{ (auth.isCustomerUser() ? d.activeDevices : d.customers) | num }}</strong>
              <small>{{ 'dash.devicesN' | t: { n: d.activeDevices } }}</small>
            </a>
          }
          <a class="kpi" routerLink="/licenses">
            <div class="kpi-top"><span>{{ 'dash.licensesActive' | t }}</span><app-icon name="key" class="c-green" /></div>
            <strong>{{ d.activeLicenses | num }}</strong>
            <small class="up"><app-icon name="trendUp" [size]="14" />{{ 'dash.devicesN' | t: { n: d.activeDevices } }}</small>
          </a>
          <a class="kpi" routerLink="/subscriptions">
            <div class="kpi-top"><span>{{ 'dash.runningSubs' | t }}</span><app-icon name="invoice" class="c-violet" /></div>
            <strong>{{ d.activeSubscriptions + d.trialSubscriptions | num }}</strong>
            <small>{{ 'dash.trialsN' | t: { n: d.trialSubscriptions } }}</small>
          </a>
          <a class="kpi" [class.warn]="d.expiringIn30Days > 0" routerLink="/reports">
            <div class="kpi-top"><span>{{ 'dash.expiring' | t }}</span><app-icon name="alert" class="c-amber" /></div>
            <strong>{{ d.expiringIn30Days | num }}</strong>
            @if (d.expiringIn7Days > 0) { <small class="urgent">{{ 'dash.urgent' | t }}</small> }
            @else { <small>{{ 'dash.within30' | t: { n: d.expiringIn30Days } }}</small> }
          </a>
        </section>

        <div class="grid-main-side">
          <section class="card">
            <div class="card-head"><h2>{{ 'dash.recentLicenses' | t }}</h2><a routerLink="/licenses">{{ 'common.view' | t }}</a></div>
            <app-state [loading]="recent.loading() && !recent.data()" [error]="recent.error()" [empty]="recent.data()?.total === 0" (retry)="recent.load()">
              <div class="table-wrap">
                <table>
                  <thead><tr><th>{{ 'lic.number' | t }}</th><th>{{ 'common.customer' | t }}</th><th>{{ 'common.product' | t }}</th><th>{{ 'common.status' | t }}</th></tr></thead>
                  <tbody>
                    @for (l of recent.data()?.items; track l.id) {
                      <tr>
                        <td><a [routerLink]="['/licenses', l.id]" class="mono" dir="ltr">{{ l.licenseNumber }}</a></td>
                        <td>{{ l.customerName }}</td>
                        <td>{{ l.productCode }} / {{ l.planCode }}</td>
                        <td><app-status [value]="l.status" /></td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </app-state>
          </section>

          <section class="card">
            <h2>{{ 'dash.alerts' | t }}</h2>
            @for (a of alerts(); track a.title) {
              <a class="alert" [class]="'alert alert-' + a.tone" [routerLink]="a.link" [queryParams]="a.params ?? {}">
                <strong>{{ a.title | t }}</strong>
                <p>{{ a.body | t: { n: a.n } }}</p>
              </a>
            } @empty {
              <div class="alert alert-info"><strong>{{ 'alert.none' | t }}</strong></div>
            }
            <a class="btn block" routerLink="/reports">{{ 'dash.viewAlerts' | t }}</a>
          </section>
        </div>

        <section class="card">
          <h2>{{ 'dash.trend' | t }}</h2>
          <div class="legend">
            <span><i class="dot ok"></i>{{ 'dash.successful' | t }}</span>
            <span><i class="dot bad"></i>{{ 'dash.failed' | t }}</span>
          </div>
          <div class="bars" role="img" [attr.aria-label]="'dash.trend' | t">
            @for (p of d.activationsTrend; track p.day) {
              <div class="bar-col" [title]="(p.day | date2) + ': ' + p.successful + ' / ' + p.failed">
                <div class="bar-stack">
                  <div class="bar bad" [style.height.%]="pct(p.failed)"></div>
                  <div class="bar ok" [style.height.%]="pct(p.successful)"></div>
                </div>
                <small>{{ p.day.slice(8, 10) }}</small>
              </div>
            }
          </div>
        </section>
      }
    </app-state>
  `,
})
export class DashboardPage {
  private api = inject(Api);
  readonly auth = inject(AuthService);
  readonly data = loader(() => this.api.dashboard());
  readonly recent = loader(() => this.api.licenses({ pageSize: 5 }));
  readonly d = computed(() => this.data.data());
  private readonly max = computed(() => Math.max(1, ...(this.d()?.activationsTrend ?? []).map(p => p.successful + p.failed)));
  pct(v: number) { return (v / this.max()) * 100; }

  readonly alerts = computed<AlertItem[]>(() => {
    const d = this.d();
    if (!d) return [];
    const list: AlertItem[] = [];
    if (d.failedActivations7Days > 0)
      list.push({ tone: 'bad', title: 'alert.failed.title', body: 'alert.failed.body', n: d.failedActivations7Days, link: '/reports', params: { tab: 'failed' } });
    if (d.expiringIn7Days > 0)
      list.push({ tone: 'warn', title: 'alert.expiring.title', body: 'alert.expiring.body', n: d.expiringIn7Days, link: '/reports' });
    if (d.suspendedLicenses > 0)
      list.push({ tone: 'warn', title: 'alert.suspended.title', body: 'alert.suspended.body', n: d.suspendedLicenses, link: '/licenses' });
    if (d.revokedLicenses > 0)
      list.push({ tone: 'info', title: 'alert.revoked.title', body: 'alert.revoked.body', n: d.revokedLicenses, link: '/licenses' });
    return list;
  });
}
