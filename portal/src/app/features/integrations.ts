import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { ApiClient, ApiScopes } from '../core/api.models';
import { I18n, LocalDatePipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Modal, PageHead, SecretBox, StateView, StatusBadge, loader } from '../shared/ui';
import { Icon } from '../shared/icon';
import { computed } from '@angular/core';

@Component({
  selector: 'app-integrations',
  imports: [FormsModule, TranslatePipe, LocalDatePipe, StateView, StatusBadge, Modal, SecretBox, PageHead, Icon],
  template: `
    <app-page-head title="api.title" en="API Clients &amp; Integrations" subtitle="api.sub">
      @if (auth.can(auth.perm.ApiClientsManage)) {
        <button class="btn btn-primary" type="button" (click)="openNew()"><app-icon name="key" [size]="16" />{{ 'api.new' | t }}</button>
      }
    </app-page-head>

    @if (secret(); as s) {
      <app-secret [title]="('api.secretOnce' | t) + ' — ' + s.client.clientId" [hint]="'api.secretHint' | t" [value]="s.clientSecret" />
    }

    <div class="grid-main-side">
      <div>
        @if (auth.can(auth.perm.ApiClientsManage)) {
          <section class="card">
            <h2>{{ 'api.registered' | t }} @if (i18n.lang() === 'ar') { <span dir="ltr">(API Clients)</span> }</h2>
            <app-state [loading]="clients.loading() && !clients.data()" [error]="clients.error()" [empty]="clients.data()?.length === 0" (retry)="clients.load()">
              <div class="table-wrap">
                <table>
                  <thead><tr>
                    <th>{{ 'api.clientName' | t }}</th><th>Client ID</th><th>{{ 'api.lastUsed' | t }}</th><th>{{ 'common.status' | t }}</th><th>{{ 'common.actions' | t }}</th>
                  </tr></thead>
                  <tbody>
                    @for (c of clients.data(); track c.id) {
                      <tr>
                        <td><strong>{{ c.name }}</strong>
                          <div class="chips" style="margin-top:4px">@for (s of c.scopes; track s) { <span class="chip" dir="ltr">{{ s }}</span> }</div></td>
                        <td><span class="mono" dir="ltr">{{ c.clientId }}</span></td>
                        <td>{{ c.lastUsedAt | date2: true }}</td>
                        <td><app-status [value]="c.status" /></td>
                        <td class="actions">
                          @if (c.status !== 'Revoked') {
                            <button class="btn btn-sm" type="button" (click)="rotate(c)">{{ 'api.rotate' | t }} (Rotate)</button>
                            @if (c.status === 'Active') { <button class="btn btn-sm" type="button" (click)="act(c, 'disable')">{{ 'api.disable' | t }}</button> }
                            @else { <button class="btn btn-sm" type="button" (click)="act(c, 'enable')">{{ 'api.enable' | t }}</button> }
                            <button class="btn btn-sm btn-danger" type="button" (click)="act(c, 'revoke')">{{ 'api.revoke' | t }}</button>
                          }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </app-state>
          </section>
        }

        <section class="card">
          <div class="card-head">
            <h2>{{ 'api.signingKeys' | t }}</h2>
            @if (auth.can(auth.perm.SigningKeysManage)) {
              <button class="btn btn-sm" type="button" (click)="rotateKey()"><app-icon name="refresh" [size]="14" />{{ 'api.rotateKey' | t }}</button>
            }
          </div>
          <div class="settings-list">
            @for (k of keys.data()?.keys; track k.kid) {
              <div><span class="mono" dir="ltr">{{ k.kid }} · {{ k.alg }}</span><span class="badge" [class.ok]="k.status === 'active'" [class.warn]="k.status !== 'active'">{{ k.status }}</span></div>
            }
          </div>
        </section>
      </div>

      <section class="card">
        <h2>{{ 'api.usage' | t }}</h2>
        @if (usage(); as u) {
          <div class="progress-row"><span>{{ 'api.successRate' | t }}</span><strong class="ok" dir="ltr">{{ u.rate }}%</strong></div>
          <div class="progress ok"><span [style.width.%]="u.rate"></span></div>
          <div class="progress-row"><span>{{ 'dash.successful' | t }} / {{ 'dash.failed' | t }}</span><strong dir="ltr">{{ u.ok }} / {{ u.failed }}</strong></div>
          <div class="progress"><span [style.width.%]="u.total ? (u.ok / u.total) * 100 : 0"></span></div>
          <div class="stat-box"><small>{{ 'api.devicesLoad' | t }}</small><strong>{{ u.devices }}</strong></div>
        }
      </section>
    </div>

    <app-modal [(open)]="open" [title]="'api.new' | t">
      <form class="form" (ngSubmit)="create()">
        <label class="field"><span>{{ 'common.name' | t }}</span><input name="name" required [(ngModel)]="name" /></label>
        <fieldset class="field">
          <legend>{{ 'api.scopes' | t }}</legend>
          @for (s of allScopes; track s) {
            <label class="check" dir="ltr"><input type="checkbox" [checked]="scopes.has(s)" (change)="toggle(s)" /> {{ s }}</label>
          }
        </fieldset>
        <div class="form-actions"><button class="btn btn-primary" type="submit" [disabled]="!name || scopes.size === 0">{{ 'common.save' | t }}</button></div>
      </form>
    </app-modal>
  `,
})
export class IntegrationsPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  readonly i18n = inject(I18n);
  readonly auth = inject(AuthService);
  readonly clients = loader(() => this.api.apiClients(), this.auth.can(this.auth.perm.ApiClientsManage));
  readonly keys = loader(() => this.api.signingKeys());
  private readonly dash = loader(() => this.api.dashboard());
  readonly usage = computed(() => {
    const d = this.dash.data();
    if (!d) return null;
    const total = d.successfulActivations7Days + d.failedActivations7Days;
    return { ok: d.successfulActivations7Days, failed: d.failedActivations7Days, total, devices: d.activeDevices,
      rate: total ? Math.round((d.successfulActivations7Days / total) * 1000) / 10 : 100 };
  });
  readonly secret = signal<{ client: ApiClient; clientSecret: string } | null>(null);

  readonly allScopes = ApiScopes;
  readonly open = signal(false);
  name = '';
  scopes = new Set<string>(['licenses.activate', 'licenses.validate']);

  openNew() { this.name = ''; this.scopes = new Set(['licenses.activate', 'licenses.validate']); this.open.set(true); }
  toggle(s: string) { this.scopes.has(s) ? this.scopes.delete(s) : this.scopes.add(s); }

  create() {
    this.api.createApiClient({ name: this.name, scopes: [...this.scopes] }).subscribe(r => {
      this.secret.set(r); this.open.set(false); this.clients.load();
    });
  }

  rotate(c: ApiClient) {
    if (!confirm(this.i18n.t('common.confirm'))) return;
    this.api.rotateSecret(c.id).subscribe(r => { this.secret.set(r); this.clients.load(); });
  }

  act(c: ApiClient, action: 'disable' | 'enable' | 'revoke') {
    if (action === 'revoke' && !confirm(this.i18n.t('common.confirm'))) return;
    this.api.apiClientAction(c.id, action).subscribe(() => { this.toasts.success(this.i18n.t('common.saved')); this.clients.load(); });
  }

  rotateKey() {
    if (!confirm(this.i18n.t('common.confirm'))) return;
    this.api.rotateSigningKey().subscribe(() => { this.toasts.success(this.i18n.t('common.saved')); this.keys.load(); });
  }
}
