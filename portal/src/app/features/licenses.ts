import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { LicenseStatus } from '../core/api.models';
import { LocalDatePipe, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { PageHead, Pager, StateView, StatusBadge, loader } from '../shared/ui';
import { Icon } from '../shared/icon';

@Component({
  selector: 'app-licenses',
  imports: [FormsModule, RouterLink, TranslatePipe, LocalDatePipe, LocalNumberPipe, StateView, StatusBadge, Pager, PageHead, Icon],
  template: `
    <app-page-head title="lic.title" en="Licenses List" subtitle="lic.sub">
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
      <app-pager [(page)]="page" [total]="list.data()?.total ?? 0" />
    </app-state>
  `,
})
export class LicensesPage {
  private api = inject(Api);
  readonly router = inject(Router);
  readonly auth = inject(AuthService);
  readonly statuses: LicenseStatus[] = ['Active', 'Suspended', 'Revoked', 'Expired'];
  readonly search = signal('');
  readonly status = signal<LicenseStatus | ''>('');
  readonly page = signal(1);
  readonly list = loader(() => this.api.licenses({ search: this.search(), status: this.status(), page: this.page() }), false);

  constructor() {
    effect(() => { this.search(); this.status(); this.page(); this.list.load(); });
  }
}
