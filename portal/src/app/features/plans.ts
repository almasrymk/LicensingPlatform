import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Plan, SavePlan } from '../core/api.models';
import { I18n, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Icon } from '../shared/icon';
import { Modal, StateView, StatusBadge, loader } from '../shared/ui';

/** Plans of one product as pricing cards (mockup "Monitor Agent - Plans"), the middle published plan highlighted. */
@Component({
  selector: 'app-plans',
  imports: [FormsModule, RouterLink, TranslatePipe, LocalNumberPipe, StateView, StatusBadge, Modal, Icon],
  template: `
    <nav class="crumbs"><a routerLink="/products">{{ 'nav.products' | t }}</a><span>/</span><span>{{ product()?.name }}</span><span>/</span><span>{{ 'nav.plans' | t }}</span></nav>
    <header class="page-head">
      <div>
        <h1>{{ product() ? ('plans.forProduct' | t: { name: product()!.name }) : ('plans.title' | t) }}</h1>
        <p class="subtitle">{{ 'plans.sub' | t }}</p>
      </div>
      <div class="actions">
        <select [ngModel]="productId()" (ngModelChange)="productId.set($event)" [attr.aria-label]="'col.product' | t">
          @for (p of products.data()?.items; track p.id) { <option [value]="p.id">{{ p.name }}</option> }
        </select>
        @if (canManage() && product()) {
          <button class="btn btn-primary" type="button" (click)="newPlan()"><app-icon name="plus" [size]="16" />{{ 'plans.new' | t }}</button>
        }
      </div>
    </header>

    <app-state [loading]="plans.loading() && !plans.data()" [error]="plans.error()" [empty]="plans.data()?.total === 0" (retry)="plans.load()">
      <div class="pricing">
        @for (pl of ordered(); track pl.id) {
          <article class="price-card" [class.popular]="pl.id === popularId()" [class.dim]="pl.status === 'Archived'">
            @if (pl.id === popularId()) { <span class="ribbon">{{ 'plans.mostPopular' | t }}</span> }
            <h3>{{ pl.name }}</h3>
            <div class="amount" dir="ltr">
              @if (pl.price > 0) { {{ pl.price | num }} {{ pl.currency }} <small>{{ cycle(pl) }}</small> }
              @else { {{ pl.trialDays ? ('plans.trialN' | t: { n: pl.trialDays }) : '0' }} }
            </div>
            <div class="meta"><span class="mono" dir="ltr">{{ pl.code }} v{{ pl.version }}</span> · <app-status [value]="pl.status" /></div>
            <ul>
              <li><app-icon name="check" [size]="16" />{{ pl.maxActivations ? ('plans.devicesN' | t: { n: pl.maxActivations }) : ('plans.unlimitedDevices' | t) }}</li>
              <li><app-icon name="check" [size]="16" />{{ pl.durationDays ? ('common.days' | t: { n: pl.durationDays }) : ('plans.lifetimeAccess' | t) }}</li>
              @if (pl.trialDays) { <li><app-icon name="check" [size]="16" />{{ 'plans.trialN' | t: { n: pl.trialDays } }}</li> }
              <li><app-icon name="check" [size]="16" />{{ 'plans.heartbeatN' | t: { n: pl.heartbeatIntervalHours } }}</li>
              <li><app-icon name="check" [size]="16" />{{ 'plans.offlineN' | t: { n: pl.offlineGraceDays } }}</li>
              @for (f of pl.features; track f) { <li><app-icon name="check" [size]="16" /><span dir="ltr">{{ f }}</span></li> }
            </ul>
            @if (canManage()) {
              @if (pl.status === 'Draft') {
                <button class="btn" type="button" (click)="editPlan(pl)">{{ 'plans.manage' | t }}</button>
                <button class="btn btn-primary" type="button" (click)="act(pl, 'publish')">{{ 'plans.publish' | t }}</button>
              } @else if (pl.status === 'Published') {
                <button class="btn" type="button" (click)="act(pl, 'version')">{{ 'plans.newVersion' | t }}</button>
                <button class="btn btn-ghost" type="button" (click)="act(pl, 'archive')">{{ 'plans.archive' | t }}</button>
              }
            }
          </article>
        }
      </div>
    </app-state>

    <app-modal [(open)]="planOpen" [title]="(editingPlan() ? 'common.edit' : 'plans.new') | t" wide>
      <form class="form" (ngSubmit)="savePlan()">
        <div class="grid-2">
          <label class="field"><span>{{ 'common.code' | t }}</span><input name="code" dir="ltr" required [disabled]="!!editingPlan()" [(ngModel)]="form.code" /></label>
          <label class="field"><span>{{ 'common.name' | t }}</span><input name="name" required [(ngModel)]="form.name" /></label>
          <label class="field"><span>{{ 'plans.price' | t }}</span><input name="price" type="number" min="0" step="0.01" required [(ngModel)]="form.price" /></label>
          <label class="field"><span>{{ 'plans.currency' | t }}</span><input name="currency" dir="ltr" maxlength="3" required [(ngModel)]="form.currency" /></label>
          <label class="field"><span>{{ 'plans.duration' | t }}</span><input name="duration" type="number" min="1" [(ngModel)]="form.durationDays" />
            <small class="muted">{{ 'plans.durationHint' | t }}</small></label>
          <label class="field"><span>{{ 'plans.trial' | t }}</span><input name="trial" type="number" min="0" max="90" [(ngModel)]="form.trialDays" /></label>
          <label class="field"><span>{{ 'plans.maxActivations' | t }}</span><input name="max" type="number" min="1" [(ngModel)]="form.maxActivations" />
            <small class="muted">{{ 'plans.maxHint' | t }}</small></label>
          <label class="field"><span>{{ 'plans.heartbeat' | t }}</span><input name="hb" type="number" min="1" max="720" required [(ngModel)]="form.heartbeatIntervalHours" /></label>
          <label class="field"><span>{{ 'plans.grace' | t }}</span><input name="grace" type="number" min="0" max="365" required [(ngModel)]="form.offlineGraceDays" /></label>
        </div>
        <label class="field"><span>{{ 'plans.features' | t }}</span><input name="features" dir="ltr" [(ngModel)]="featuresText" placeholder="reports, export, multi-branch" /></label>
        <div class="form-actions"><button class="btn btn-primary" type="submit">{{ 'common.save' | t }}</button></div>
      </form>
    </app-modal>
  `,
})
export class PlansPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private route = inject(ActivatedRoute);
  readonly i18n = inject(I18n);
  readonly auth = inject(AuthService);
  readonly canManage = computed(() => this.auth.can(this.auth.perm.CatalogManage));

  readonly products = loader(() => this.api.products({ pageSize: 200 }));
  readonly productId = signal<string>(this.route.snapshot.queryParamMap.get('product') ?? '');
  readonly product = computed(() => (this.products.data()?.items ?? []).find(p => p.id === this.productId()) ?? null);
  readonly plans = loader(() => this.api.plans({ productId: this.productId(), pageSize: 200 }), false);

  /** Cheapest first, archived last, like a public pricing page. */
  readonly ordered = computed(() => [...(this.plans.data()?.items ?? [])]
    .sort((a, b) => (a.status === 'Archived' ? 1 : 0) - (b.status === 'Archived' ? 1 : 0) || a.price - b.price));
  /** The middle published plan carries the "Most Popular" ribbon. */
  readonly popularId = computed(() => {
    const published = this.ordered().filter(p => p.status === 'Published');
    return published.length >= 3 ? published[Math.floor(published.length / 2)].id : null;
  });

  readonly planOpen = signal(false);
  readonly editingPlan = signal<Plan | null>(null);
  form: SavePlan = this.empty('');
  featuresText = '';

  constructor() {
    effect(() => {
      const items = this.products.data()?.items ?? [];
      if (items.length && !items.some(p => p.id === this.productId())) this.productId.set(items[0].id);
    });
    effect(() => { if (this.productId()) this.plans.load(); });
  }

  cycle(pl: Plan): string {
    if (!pl.durationDays) return this.i18n.t('plans.oneTime');
    if (pl.durationDays <= 31) return this.i18n.t('plans.perMonth');
    if (pl.durationDays === 365) return this.i18n.t('plans.perYear');
    return this.i18n.t('plans.perDays', { n: pl.durationDays });
  }

  private ok() { this.toasts.success(this.i18n.t('common.saved')); }

  private empty(productId: string): SavePlan {
    return { productId, code: '', name: '', price: 0, currency: 'EGP', durationDays: 30, trialDays: null, maxActivations: 1,
      heartbeatIntervalHours: 24, offlineGraceDays: 7, features: [] };
  }

  newPlan() { this.editingPlan.set(null); this.form = this.empty(this.productId()); this.featuresText = ''; this.planOpen.set(true); }

  editPlan(pl: Plan) {
    this.editingPlan.set(pl);
    this.form = { productId: pl.productId, code: pl.code, name: pl.name, price: pl.price, currency: pl.currency,
      durationDays: pl.durationDays ?? null, trialDays: pl.trialDays ?? null, maxActivations: pl.maxActivations ?? null,
      heartbeatIntervalHours: pl.heartbeatIntervalHours, offlineGraceDays: pl.offlineGraceDays, features: pl.features };
    this.featuresText = pl.features.join(', ');
    this.planOpen.set(true);
  }

  savePlan() {
    const nullIfEmpty = (v: unknown) => (v === '' || v === null || v === undefined ? null : Number(v));
    const body: SavePlan = {
      ...this.form, price: Number(this.form.price), durationDays: nullIfEmpty(this.form.durationDays),
      trialDays: nullIfEmpty(this.form.trialDays), maxActivations: nullIfEmpty(this.form.maxActivations),
      features: this.featuresText.split(',').map(f => f.trim()).filter(Boolean),
    };
    const e = this.editingPlan();
    (e ? this.api.updatePlan(e.id, body) : this.api.createPlan(body)).subscribe(() => { this.ok(); this.planOpen.set(false); this.plans.load(); });
  }

  act(pl: Plan, action: 'publish' | 'archive' | 'version') {
    const req = action === 'publish' ? this.api.publishPlan(pl.id) : action === 'archive' ? this.api.archivePlan(pl.id) : this.api.newPlanVersion(pl.id);
    req.subscribe(() => { this.ok(); this.plans.load(); });
  }
}
