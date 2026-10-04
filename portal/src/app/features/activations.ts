import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { ActivationRow } from '../core/api.models';
import { AgoPipe, I18n, LocalDatePipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Icon } from '../shared/icon';
import { Pager, StateView, loader } from '../shared/ui';

/** Activations / Devices from the mockup: device, OS, IP, activated, last seen, Online/Offline/Disabled. */
@Component({
  selector: 'app-activations',
  imports: [FormsModule, RouterLink, TranslatePipe, LocalDatePipe, AgoPipe, StateView, Pager, Icon],
  template: `
    <header class="page-head">
      <div><h1>{{ 'act.title' | t }}</h1><p class="subtitle">{{ 'act.sub' | t }}</p></div>
    </header>

    <div class="table-card">
      <div class="filters" style="border:0;border-radius:0;box-shadow:none;border-bottom:1px solid var(--border);margin:0">
        <div class="search">
          <app-icon name="search" [size]="16" />
          <input type="search" [placeholder]="'common.search' | t" [ngModel]="search()" (ngModelChange)="search.set($event); page.set(1)" />
        </div>
        <select [ngModel]="status()" (ngModelChange)="status.set($event); page.set(1)" [attr.aria-label]="'col.status' | t">
          <option value="">{{ 'filter.allStatus' | t }}</option>
          <option value="Active">{{ 'status.Active' | t }}</option>
          <option value="Deactivated">{{ 'act.disabled' | t }}</option>
        </select>
        <span class="spacer"></span>
        <button class="icon-btn" type="button" [attr.aria-label]="'common.refresh' | t" (click)="list.load()"><app-icon name="refresh" [size]="18" /></button>
      </div>

      <app-state [loading]="list.loading() && !list.data()" [error]="list.error()" [empty]="list.data()?.total === 0" (retry)="list.load()">
        <div class="table-wrap">
          <table>
            <thead><tr>
              <th>{{ 'col.device' | t }}</th><th>{{ 'col.os' | t }}</th><th>{{ 'col.ip' | t }}</th><th>{{ 'col.customer' | t }}</th>
              <th>{{ 'lic.number' | t }}</th><th>{{ 'col.activated' | t }}</th><th>{{ 'col.lastSeen' | t }}</th><th>{{ 'col.status' | t }}</th><th></th>
            </tr></thead>
            <tbody>
              @for (a of list.data()?.items; track a.id) {
                <tr>
                  <td><strong class="mono" dir="ltr">{{ a.deviceId }}</strong>@if (a.deviceName) { <span class="cell-sub">{{ a.deviceName }}</span> }</td>
                  <td>{{ a.operatingSystem ?? '—' }}</td>
                  <td dir="ltr">{{ a.lastIpAddress ?? '—' }}</td>
                  <td>{{ a.customerName }}</td>
                  <td><a class="mono" dir="ltr" [routerLink]="['/licenses', a.licenseId]">{{ a.licenseNumber }}</a></td>
                  <td>{{ a.activatedAt | date2 }}</td>
                  <td>{{ a.lastHeartbeatAt | ago }}</td>
                  <td>
                    @if (a.status !== 'Active') { <span class="disabled-st"><span class="status-dot bad"></span>{{ 'act.disabled' | t }}</span> }
                    @else if (a.online) { <span class="online"><span class="status-dot ok"></span>{{ 'act.online' | t }}</span> }
                    @else { <span class="offline"><span class="status-dot"></span>{{ 'act.offline' | t }}</span> }
                  </td>
                  <td>
                    @if (a.status === 'Active' && auth.can(auth.perm.LicensesManage)) {
                      <button class="btn btn-sm" type="button" (click)="reset(a)">{{ 'lic.reset' | t }}</button>
                    }
                  </td>
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
export class ActivationsPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private i18n = inject(I18n);
  readonly auth = inject(AuthService);

  readonly search = signal('');
  readonly status = signal('');
  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly list = loader(() => this.api.activations({ search: this.search(), status: this.status(), page: this.page(), pageSize: this.pageSize() }), false);

  constructor() {
    effect(() => { this.search(); this.status(); this.page(); this.pageSize(); this.list.load(); });
  }

  reset(a: ActivationRow) {
    if (!confirm(this.i18n.t('common.confirm'))) return;
    this.api.resetDevice(a.licenseId, a.id).subscribe(() => { this.toasts.success(this.i18n.t('common.saved')); this.list.load(); });
  }
}
