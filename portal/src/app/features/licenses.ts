import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { LicenseStatus } from '../core/api.models';
import { LocalDatePipe, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { PageHead, Pager, StateView, StatusBadge, loader } from '../shared/ui';
import { Icon } from '../shared/icon';
import { lookupSignals } from '../shared/media';

@Component({
  selector: 'app-licenses',
  imports: [FormsModule, RouterLink, TranslatePipe, LocalDatePipe, LocalNumberPipe, StateView, StatusBadge, Pager, PageHead, Icon],
  template: `
    <app-page-head title="lic.title" en="Licenses List" subtitle="lic.sub">
    </app-page-head>

    <div class="table-card">
    <div class="filters" style="border:0;border-radius:0;box-shadow:none;border-bottom:1px solid var(--border);margin:0">
      <div class="search">
        <app-icon name="search" [size]="16" />
        <input type="search" [placeholder]="'common.search' | t" [ngModel]="search()" (ngModelChange)="search.set($event); page.set(1)" />
      </div>
      <select [ngModel]="status()" (ngModelChange)="status.set($event); page.set(1)" [attr.aria-label]="'common.status' | t">
        <option value="">{{ 'common.all' | t }}</option>
        @for (s of statuses; track s) { <option [value]="s">{{ 'status.' + s | t }}</option> }
      </select>
      <select [ngModel]="productId()" (ngModelChange)="productId.set($event); planId.set(''); page.set(1)" [attr.aria-label]="'common.product' | t">
        <option value="">{{ 'filter.allProducts' | t }}</option>
        @for (p of lk.products(); track p.id) { <option [value]="p.id">{{ p.name }}</option> }
      </select>
      <select [ngModel]="planId()" (ngModelChange)="planId.set($event); page.set(1)" [attr.aria-label]="'common.plan' | t">
        <option value="">{{ 'filter.allPlans' | t }}</option>
        @for (p of planOptions(); track p.id) { <option [value]="p.id">{{ p.name }} v{{ p.version }}</option> }
      </select>
      @if (auth.isPlatformAdmin()) {
        <select [ngModel]="tenantId()" (ngModelChange)="tenantId.set($event); page.set(1)" [attr.aria-label]="'common.tenant' | t">
          <option value="">{{ 'filter.allTenants' | t }}</option>
          @for (t of lk.tenants(); track t.id) { <option [value]="t.id">{{ t.name }}</option> }
        </select>
      }
      <button type="button" class="chk" [class.on]="nearExpiry()" [attr.aria-pressed]="nearExpiry()" (click)="nearExpiry.set(!nearExpiry()); page.set(1)"><app-icon name="clock" [size]="14" />{{ 'filter.nearExpiry' | t }}</button>
      <button type="button" class="chk" [class.on]="limitReached()" [attr.aria-pressed]="limitReached()" (click)="limitReached.set(!limitReached()); page.set(1)"><app-icon name="monitor" [size]="14" />{{ 'filter.limitReached' | t }}</button>
      @if (search() || status() || productId() || planId() || tenantId() || nearExpiry() || limitReached()) {
        <button class="btn btn-sm btn-ghost" type="button" (click)="clear()">{{ 'filter.clear' | t }}</button>
      }
    </div>

    <app-state [loading]="list.loading() && !list.data()" [error]="list.error()" [empty]="list.data()?.total === 0" (retry)="list.load()">
      <div class="table-wrap">
        <table>
          <thead><tr>
            <th>{{ 'lic.number' | t }}</th><th>{{ 'common.customer' | t }}</th><th>{{ 'common.product' | t }}</th>
            <th>{{ 'lic.prefix' | t }}</th><th>{{ 'common.status' | t }}</th><th>{{ 'lic.devices' | t }}</th><th>{{ 'lic.expires' | t }}</th>
          </tr></thead>
          <tbody>
            @for (l of list.data()?.items; track l.id) {
              <tr class="clickable" (click)="router.navigate(['/licenses', l.id])">
                <td><a [routerLink]="['/licenses', l.id]" dir="ltr">{{ l.licenseNumber }}</a></td>
                <td>{{ l.customerName }}</td>
                <td>{{ l.productCode }} / {{ l.planCode }} <small class="muted">v{{ l.planVersion }}</small></td>
                <td dir="ltr"><code>{{ l.productKeyPrefix }}-••••</code></td>
                <td><app-status [value]="l.status" /></td>
                <td>
                  <div class="meter" [attr.aria-label]="'lic.used' | t: { used: l.activeActivations, max: l.maxActivations ?? '∞' }">
                    <span [style.width.%]="l.maxActivations ? (l.activeActivations / l.maxActivations) * 100 : 10"></span>
                  </div>
                  <small>{{ l.activeActivations | num }} / {{ l.maxActivations ?? '∞' }}</small>
                </td>
                <td>{{ l.expiresAt ? (l.expiresAt | date2) : ('common.lifetime' | t) }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <app-pager [(page)]="page" [(pageSize)]="pageSize" [total]="list.data()?.total ?? 0" />
    </app-state>
    </div>
  `,
})
export class LicensesPage {
  private api = inject(Api);
  readonly router = inject(Router);
  readonly auth = inject(AuthService);
  readonly statuses: LicenseStatus[] = ['Active', 'Suspended', 'Revoked', 'Expired'];
  private route = inject(ActivatedRoute);
  readonly search = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  readonly status = signal<LicenseStatus | ''>((this.route.snapshot.queryParamMap.get('status') as LicenseStatus | null) ?? '');
  readonly lk = lookupSignals();
  readonly productId = signal(this.route.snapshot.queryParamMap.get('productId') ?? '');
  readonly planId = signal('');
  readonly tenantId = signal('');
  readonly planOptions = computed(() => this.lk.plans().filter(p => !this.productId() || p.productId === this.productId()));
  readonly nearExpiry = signal(this.route.snapshot.queryParamMap.get('nearExpiry') === 'true');
  readonly limitReached = signal(this.route.snapshot.queryParamMap.get('limitReached') === 'true');
  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly list = loader(() => this.api.licenses({ search: this.search(), status: this.status(), productId: this.productId(), planId: this.planId(), tenantId: this.tenantId(),
    nearExpiry: this.nearExpiry() || null, limitReached: this.limitReached() || null, page: this.page(), pageSize: this.pageSize() }), false);

  clear() {
    this.search.set(''); this.status.set(''); this.productId.set(''); this.planId.set(''); this.tenantId.set('');
    this.nearExpiry.set(false); this.limitReached.set(false); this.page.set(1);
  }

  constructor() {
    effect(() => { this.search(); this.status(); this.productId(); this.planId(); this.tenantId(); this.nearExpiry(); this.limitReached(); this.page(); this.pageSize(); this.list.load(); });
  }
}
