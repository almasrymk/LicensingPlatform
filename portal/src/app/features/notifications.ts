import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { Icon } from '../shared/icon';
import { StateView, loader } from '../shared/ui';

/** Actionable alerts (spec "Notifications"): built from the same figures as the dashboard's "Needs Attention". */
@Component({
  selector: 'app-notifications',
  imports: [RouterLink, TranslatePipe, LocalNumberPipe, StateView, Icon],
  template: `
    <header class="page-head"><div><h1>{{ 'notif.title' | t }}</h1><p class="subtitle">{{ 'notif.sub' | t }}</p></div></header>
    <app-state [loading]="data.loading() && !data.data()" [error]="data.error()" [empty]="items().length === 0" (retry)="data.load()">
      <div class="card">
        <ul class="attention">
          @for (n of items(); track n.label) {
            <li>
              <span class="tile sm" [class]="'tile sm ' + n.tone"><app-icon [name]="n.icon" /></span>
              <div><strong>{{ n.value | num }}</strong><small>{{ n.label | t }}</small></div>
              <a [routerLink]="n.link" [queryParams]="n.params">{{ 'att.view' | t }} <app-icon name="arrowRight" class="flip" [size]="14" /></a>
            </li>
          }
        </ul>
      </div>
    </app-state>
  `,
})
export class NotificationsPage {
  private api = inject(Api);
  readonly data = loader(() => this.api.overview());
  readonly items = computed(() => {
    const a = this.data.data()?.attention;
    if (!a) return [];
    return [
      { value: a.expiredLicenses, label: 'att.expired', icon: 'alert', tone: 'red', link: '/licenses', params: { status: 'Expired' } },
      { value: a.renewalsDue, label: 'att.renewals', icon: 'invoice', tone: 'orange', link: '/reports', params: {} },
      { value: a.limitReached, label: 'att.limit', icon: 'bell', tone: 'orange', link: '/licenses', params: {} },
      { value: a.suspiciousActivations, label: 'att.suspicious', icon: 'shieldAlert', tone: 'teal', link: '/reports', params: { tab: 'failed' } },
    ].filter(n => n.value > 0);
  });
}
