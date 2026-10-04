import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../core/api.service';
import { LocalDatePipe, TranslatePipe } from '../core/i18n.service';
import { PageHead, Pager, StateView, loader } from '../shared/ui';
import { Icon } from '../shared/icon';
import { AuthService } from '../core/auth.service';
import { lookupSignals } from '../shared/media';

@Component({
  selector: 'app-audit',
  imports: [FormsModule, TranslatePipe, LocalDatePipe, StateView, Pager, PageHead, Icon],
  template: `
    <app-page-head title="audit.title" en="Audit Logs" subtitle="audit.sub">
      <button class="btn" type="button" (click)="exportCsv()" [disabled]="!list.data()?.items?.length"><app-icon name="download" [size]="16" />{{ 'audit.export' | t }}</button>
    </app-page-head>

    <div class="table-card">
    <div class="filters" style="border:0;border-radius:0;box-shadow:none;border-bottom:1px solid var(--border);margin:0">
      <div class="search">
        <app-icon name="search" [size]="16" />
        <input type="search" dir="ltr" [placeholder]="'filter.actionPh' | t" [ngModel]="action()" (ngModelChange)="action.set($event); page.set(1)" [attr.aria-label]="'audit.action' | t" />
      </div>
      <div class="search">
        <app-icon name="user" [size]="16" />
        <input type="search" [placeholder]="'filter.userPh' | t" [ngModel]="actor()" (ngModelChange)="actor.set($event); page.set(1)" [attr.aria-label]="'audit.actor' | t" />
      </div>
      <select [ngModel]="entityType()" (ngModelChange)="entityType.set($event); page.set(1)" [attr.aria-label]="'audit.entity' | t">
        <option value="">{{ 'filter.allEntities' | t }}</option>
        @for (e of entities; track e) { <option [value]="e">{{ e }}</option> }
      </select>
      @if (auth.isPlatformAdmin()) {
        <select [ngModel]="tenantId()" (ngModelChange)="tenantId.set($event); page.set(1)" [attr.aria-label]="'common.tenant' | t">
          <option value="">{{ 'filter.allTenants' | t }}</option>
          @for (t of lk.tenants(); track t.id) { <option [value]="t.id">{{ t.name }}</option> }
        </select>
      }
      <select [ngModel]="success()" (ngModelChange)="success.set($event); page.set(1)" [attr.aria-label]="'audit.result' | t">
        <option value="">{{ 'filter.allResults' | t }}</option>
        <option value="true">{{ 'audit.success' | t }}</option>
        <option value="false">{{ 'audit.failure' | t }}</option>
      </select>
      <label class="date-pair"><span>{{ 'filter.date' | t }}</span><input type="date" [ngModel]="from()" (ngModelChange)="from.set($event); page.set(1)" />
        <span>{{ 'filter.to' | t }}</span><input type="date" [ngModel]="to()" (ngModelChange)="to.set($event); page.set(1)" /></label>
      @if (action() || actor() || entityType() || tenantId() || success() || from() || to()) {
        <button class="btn btn-sm btn-ghost" type="button" (click)="clear()">{{ 'filter.clear' | t }}</button>
      }
    </div>

    <app-state [loading]="list.loading() && !list.data()" [error]="list.error()" [empty]="list.data()?.total === 0" (retry)="list.load()">
      <div class="table-wrap">
        <table>
          <thead><tr>
            <th>{{ 'audit.at' | t }}</th><th>{{ 'audit.action' | t }}</th><th>{{ 'audit.actor' | t }}</th>
            <th>{{ 'audit.entity' | t }}</th><th>{{ 'audit.result' | t }}</th><th>{{ 'common.details' | t }}</th><th>{{ 'lic.ip' | t }}</th>
          </tr></thead>
          <tbody>
            @for (a of list.data()?.items; track a.id) {
              <tr>
                <td>{{ a.at | date2: true }}</td>
                <td dir="ltr"><span class="action-code" [class]="'action-code ' + tone(a.action, a.success)">{{ a.action.toUpperCase().replaceAll('.', '_') }}</span></td>
                <td dir="ltr">{{ a.actorName ?? a.actorType }}</td>
                <td dir="ltr"><small>{{ a.entityType }} {{ a.entityId?.slice(0, 8) }}</small></td>
                <td><span class="badge" [class.ok]="a.success" [class.bad]="!a.success">{{ (a.success ? 'audit.success' : 'audit.failure') | t }}</span></td>
                <td><small dir="auto">{{ a.details }}</small></td>
                <td dir="ltr"><small>{{ a.ipAddress }}</small></td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <app-pager [(page)]="page" [total]="list.data()?.total ?? 0" [pageSize]="50" />
    </app-state>
    </div>
  `,
})
export class AuditPage {
  private api = inject(Api);
  readonly auth = inject(AuthService);
  readonly lk = lookupSignals();
  readonly entities = ['Tenant', 'Customer', 'Product', 'Plan', 'Subscription', 'License', 'Activation', 'User', 'ApiClient', 'SigningKey'];
  readonly actor = signal('');
  readonly entityType = signal('');
  readonly tenantId = signal('');
  readonly action = signal('');
  readonly success = signal('');
  readonly from = signal('');
  readonly to = signal('');
  readonly page = signal(1);
  readonly list = loader(() => this.api.audit({
    action: this.action(), actor: this.actor(), entityType: this.entityType(), tenantId: this.tenantId(), success: this.success(), page: this.page(), pageSize: 50,
    from: this.from() ? new Date(this.from()).toISOString() : null,
    to: this.to() ? new Date(this.to() + 'T23:59:59').toISOString() : null,
  }), false);

  /** Same color language as the design: failures red, revocations/suspensions amber, renewals and creations green. */
  tone(action: string, success: boolean): string {
    if (!success) return 'bad';
    if (/revoked|suspended|reset|rotated|deactivat|cancel/.test(action)) return 'warn';
    if (/created|issued|renewed|activated|published|resumed/.test(action)) return 'ok';
    return 'info';
  }

  clear() {
    this.action.set(''); this.actor.set(''); this.entityType.set(''); this.tenantId.set(''); this.success.set('');
    this.from.set(''); this.to.set(''); this.page.set(1);
  }

  exportCsv() {
    const rows = this.list.data()?.items ?? [];
    const esc = (v: unknown) => '"' + String(v ?? '').replaceAll('"', '""') + '"';
    const csv = ['At,Action,Actor,EntityType,EntityId,Success,Details,IP,CorrelationId',
      ...rows.map(a => [a.at, a.action, a.actorName ?? a.actorType, a.entityType, a.entityId, a.success, a.details, a.ipAddress, a.correlationId].map(esc).join(','))].join('\r\n');
    // BOM so Excel opens the Arabic text correctly.
    const url = URL.createObjectURL(new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = 'audit-log.csv';
    link.click();
    URL.revokeObjectURL(url);
  }

  constructor() {
    effect(() => { this.action(); this.actor(); this.entityType(); this.tenantId(); this.success(); this.from(); this.to(); this.page(); this.list.load(); });
  }
}
