import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../core/api.service';
import { LocalDatePipe, TranslatePipe } from '../core/i18n.service';
import { PageHead, Pager, StateView, loader } from '../shared/ui';
import { Icon } from '../shared/icon';

@Component({
  selector: 'app-audit',
  imports: [FormsModule, TranslatePipe, LocalDatePipe, StateView, Pager, PageHead, Icon],
  template: `
    <app-page-head title="audit.title" en="Audit Logs" subtitle="audit.sub">
      <button class="btn" type="button" (click)="exportCsv()" [disabled]="!list.data()?.items?.length"><app-icon name="download" [size]="16" />{{ 'audit.export' | t }}</button>
    </app-page-head>

    <div class="filters">
      <input type="search" dir="ltr" placeholder="license., auth.login, tenant." [ngModel]="action()" (ngModelChange)="action.set($event); page.set(1)" [attr.aria-label]="'audit.action' | t" />
      <select [ngModel]="success()" (ngModelChange)="success.set($event); page.set(1)" [attr.aria-label]="'audit.result' | t">
        <option value="">{{ 'common.all' | t }}</option>
        <option value="true">{{ 'audit.success' | t }}</option>
        <option value="false">{{ 'audit.failure' | t }}</option>
      </select>
      <label class="inline">{{ 'common.from' | t }} <input type="date" [ngModel]="from()" (ngModelChange)="from.set($event); page.set(1)" /></label>
      <label class="inline">{{ 'common.to' | t }} <input type="date" [ngModel]="to()" (ngModelChange)="to.set($event); page.set(1)" /></label>
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
  `,
})
export class AuditPage {
  private api = inject(Api);
  readonly action = signal('');
  readonly success = signal('');
  readonly from = signal('');
  readonly to = signal('');
  readonly page = signal(1);
  readonly list = loader(() => this.api.audit({
    action: this.action(), success: this.success(), page: this.page(), pageSize: 50,
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
    effect(() => { this.action(); this.success(); this.from(); this.to(); this.page(); this.list.load(); });
  }
}
