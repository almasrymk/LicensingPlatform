import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Plan, Product, SavePlan } from '../core/api.models';
import { I18n, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Modal, PageHead, StateView, StatusBadge, loader } from '../shared/ui';
import { Icon } from '../shared/icon';

@Component({
  selector: 'app-catalog',
  imports: [FormsModule, TranslatePipe, LocalNumberPipe, StateView, StatusBadge, Modal, PageHead, Icon],
  template: `
    <app-page-head title="products.title" en="Products &amp; Plans Catalog" subtitle="products.sub">
      @if (canManage()) {
        @if (selected(); as p) {
          <button class="btn" type="button" (click)="newPlan(p)"><app-icon name="plus" [size]="16" />{{ 'products.newPlan' | t }}</button>
        }
        <button class="btn btn-primary" type="button" (click)="newProduct()"><app-icon name="plus" [size]="16" />{{ 'products.new' | t }}</button>
      }
    </app-page-head>

    <app-state [loading]="products.loading() && !products.data()" [error]="products.error()" [empty]="products.data()?.total === 0" (retry)="products.load()">
      <div class="product-grid">
        @for (p of products.data()?.items; track p.id) {
          <button type="button" class="product-card" [class.selected]="selected()?.id === p.id" (click)="selected.set(p)">
            <header>
              <strong>{{ p.name }}</strong>
              @if (p.isActive) { <span class="badge ok">{{ 'products.available' | t }}</span> }
              @else { <span class="badge neutral">{{ 'status.Inactive' | t }}</span> }
            </header>
            <small class="muted">{{ 'products.codeLabel' | t: { c: p.code } }}</small>
            <p>{{ p.description }}</p>
            <footer>
              <span>{{ 'products.plansAvailable' | t }}: <b>{{ 'products.plansN' | t: { n: p.publishedPlans } }}</b></span>
              @if (canManage()) { <span class="link" role="link" (click)="$event.stopPropagation(); editProduct(p)">{{ 'common.edit' | t }}</span> }
            </footer>
          </button>
        }
      </div>
    </app-state>

    <section class="card">
      <div class="card-head">
        <h2>{{ 'products.matrix' | t }} @if (i18n.lang() === 'ar') { <span dir="ltr">(Plan Pricing &amp; Entitlements)</span> }</h2>
        @if (selected(); as p) { <span class="badge info">{{ p.name }}</span> }
      </div>
      <app-state [loading]="plans.loading() && !plans.data()" [error]="plans.error()" [empty]="plans.data()?.total === 0" (retry)="plans.load()">
        <div class="table-wrap">
          <table>
            <thead><tr>
              <th>{{ 'plans.planName' | t }}</th><th>{{ 'plans.productCol' | t }}</th><th>{{ 'plans.cycle' | t }}</th>
              <th>{{ 'plans.price' | t }}</th><th>{{ 'plans.entitlementsCol' | t }}</th><th>{{ 'common.status' | t }}</th><th>{{ 'common.actions' | t }}</th>
            </tr></thead>
            <tbody>
              @for (pl of plans.data()?.items; track pl.id) {
                <tr [class.dim]="pl.status === 'Archived'">
                  <td><strong>{{ pl.name }}</strong><span class="cell-sub mono" dir="ltr">{{ pl.code }} v{{ pl.version }}</span></td>
                  <td>{{ pl.productName }}</td>
                  <td>{{ cycle(pl) }}
                    @if (pl.trialDays) { <span class="cell-sub">{{ 'plans.trial' | t }}: {{ pl.trialDays }}</span> }</td>
                  <td dir="ltr" class="price-cell"><strong>{{ pl.price | num }}</strong> {{ pl.currency }}</td>
                  <td>
                    <span>{{ pl.maxActivations ? ('plans.devicesUpTo' | t: { n: pl.maxActivations }) : ('plans.devicesUnlimited' | t) }}</span>
                    <div class="chips">@for (f of pl.features; track f) { <span class="chip" dir="ltr">{{ f }}</span> }</div>
                  </td>
                  <td><app-status [value]="pl.status" /></td>
                  <td class="actions">
                    @if (canManage()) {
                      @if (pl.status === 'Draft') {
                        <button class="btn btn-sm" type="button" (click)="editPlan(pl)">{{ 'plans.editPlan' | t }}</button>
                        <button class="btn btn-sm btn-primary" type="button" (click)="planAction(pl, 'publish')">{{ 'plans.publish' | t }}</button>
                      }
                      @if (pl.status === 'Published') {
                        <button class="btn btn-sm" type="button" (click)="planAction(pl, 'version')">{{ 'plans.newVersion' | t }}</button>
                        <button class="btn btn-sm" type="button" (click)="planAction(pl, 'archive')">{{ 'plans.archive' | t }}</button>
                      }
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </app-state>
    </section>

    <app-modal [(open)]="productOpen" [title]="(editingProduct() ? 'common.edit' : 'products.new') | t">
      <form class="form" (ngSubmit)="saveProduct()">
        <label class="field"><span>{{ 'common.code' | t }}</span><input name="code" dir="ltr" required [disabled]="!!editingProduct()" [(ngModel)]="pForm.code" /></label>
        <label class="field"><span>{{ 'common.name' | t }}</span><input name="name" required [(ngModel)]="pForm.name" /></label>
        <label class="field"><span>{{ 'common.description' | t }}</span><textarea name="desc" rows="3" [(ngModel)]="pForm.description"></textarea></label>
        @if (editingProduct()) { <label class="check"><input type="checkbox" name="active" [(ngModel)]="pForm.isActive" /> {{ 'common.active' | t }}</label> }
        <div class="form-actions"><button class="btn btn-primary" type="submit">{{ 'common.save' | t }}</button></div>
      </form>
    </app-modal>

    <app-modal [(open)]="planOpen" [title]="(editingPlan() ? 'common.edit' : 'products.newPlan') | t" wide>
      <form class="form" (ngSubmit)="savePlan()">
        <div class="grid-2">
          <label class="field"><span>{{ 'common.code' | t }}</span><input name="code" dir="ltr" required [disabled]="!!editingPlan()" [(ngModel)]="plForm.code" /></label>
          <label class="field"><span>{{ 'common.name' | t }}</span><input name="name" required [(ngModel)]="plForm.name" /></label>
          <label class="field"><span>{{ 'plans.price' | t }}</span><input name="price" type="number" min="0" step="0.01" required [(ngModel)]="plForm.price" /></label>
          <label class="field"><span>{{ 'plans.currency' | t }}</span><input name="currency" dir="ltr" maxlength="3" required [(ngModel)]="plForm.currency" /></label>
          <label class="field"><span>{{ 'plans.duration' | t }}</span><input name="duration" type="number" min="1" [(ngModel)]="plForm.durationDays" />
            <small class="muted">{{ 'plans.durationHint' | t }}</small></label>
          <label class="field"><span>{{ 'plans.trial' | t }}</span><input name="trial" type="number" min="0" max="90" [(ngModel)]="plForm.trialDays" /></label>
          <label class="field"><span>{{ 'plans.maxActivations' | t }}</span><input name="max" type="number" min="1" [(ngModel)]="plForm.maxActivations" />
            <small class="muted">{{ 'plans.maxHint' | t }}</small></label>
          <label class="field"><span>{{ 'plans.heartbeat' | t }}</span><input name="hb" type="number" min="1" max="720" required [(ngModel)]="plForm.heartbeatIntervalHours" /></label>
          <label class="field"><span>{{ 'plans.grace' | t }}</span><input name="grace" type="number" min="0" max="365" required [(ngModel)]="plForm.offlineGraceDays" /></label>
        </div>
        <label class="field"><span>{{ 'plans.features' | t }}</span><input name="features" dir="ltr" [(ngModel)]="featuresText" placeholder="reports, export, multi-branch" /></label>
        <div class="form-actions"><button class="btn btn-primary" type="submit">{{ 'common.save' | t }}</button></div>
      </form>
    </app-modal>
  `,
})
export class CatalogPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  readonly i18n = inject(I18n);
  readonly auth = inject(AuthService);
  readonly canManage = computed(() => this.auth.can(this.auth.perm.CatalogManage));

  readonly products = loader(() => this.api.products({ pageSize: 200 }));
  readonly selected = signal<Product | null>(null);
  readonly plans = loader(() => this.api.plans({ productId: this.selected()?.id, pageSize: 200 }), false);

  readonly productOpen = signal(false);
  readonly editingProduct = signal<Product | null>(null);
  pForm = { code: '', name: '', description: '', isActive: true };

  readonly planOpen = signal(false);
  readonly editingPlan = signal<Plan | null>(null);
  plForm: SavePlan = this.emptyPlan('');
  featuresText = '';

  constructor() {
    effect(() => {
      const items = this.products.data()?.items ?? [];
      const current = this.selected();
      if (items.length && (!current || !items.some(p => p.id === current.id))) this.selected.set(items[0]);
    });
    effect(() => { if (this.selected()) this.plans.load(); });
  }

  cycle(pl: Plan): string {
    if (!pl.durationDays) return this.i18n.t('common.lifetime');
    if (pl.durationDays === 30 || pl.durationDays === 31) return this.i18n.t('plans.cycleMonthly');
    if (pl.durationDays === 365) return this.i18n.t('plans.cycleYearly');
    return this.i18n.t('plans.cycleDays', { n: pl.durationDays });
  }

  private ok() { this.toasts.success(this.i18n.t('common.saved')); }

  newProduct() { this.editingProduct.set(null); this.pForm = { code: '', name: '', description: '', isActive: true }; this.productOpen.set(true); }
  editProduct(p: Product) {
    this.editingProduct.set(p);
    this.pForm = { code: p.code, name: p.name, description: p.description ?? '', isActive: p.isActive };
    this.productOpen.set(true);
  }

  saveProduct() {
    const e = this.editingProduct();
    const req = e ? this.api.updateProduct(e.id, this.pForm) : this.api.createProduct(this.pForm);
    req.subscribe(p => { this.ok(); this.productOpen.set(false); this.products.load(); this.selected.set(p); });
  }

  private emptyPlan(productId: string): SavePlan {
    return { productId, code: '', name: '', price: 0, currency: 'EGP', durationDays: 365, trialDays: null, maxActivations: 1,
      heartbeatIntervalHours: 24, offlineGraceDays: 7, features: [] };
  }

  newPlan(p: Product) { this.editingPlan.set(null); this.plForm = this.emptyPlan(p.id); this.featuresText = ''; this.planOpen.set(true); }

  editPlan(pl: Plan) {
    this.editingPlan.set(pl);
    this.plForm = { productId: pl.productId, code: pl.code, name: pl.name, price: pl.price, currency: pl.currency,
      durationDays: pl.durationDays ?? null, trialDays: pl.trialDays ?? null, maxActivations: pl.maxActivations ?? null,
      heartbeatIntervalHours: pl.heartbeatIntervalHours, offlineGraceDays: pl.offlineGraceDays, features: pl.features };
    this.featuresText = pl.features.join(', ');
    this.planOpen.set(true);
  }

  savePlan() {
    const nullIfEmpty = (v: unknown) => (v === '' || v === null || v === undefined ? null : Number(v));
    const body: SavePlan = {
      ...this.plForm,
      price: Number(this.plForm.price),
      durationDays: nullIfEmpty(this.plForm.durationDays),
      trialDays: nullIfEmpty(this.plForm.trialDays),
      maxActivations: nullIfEmpty(this.plForm.maxActivations),
      features: this.featuresText.split(',').map(f => f.trim()).filter(Boolean),
    };
    const e = this.editingPlan();
    (e ? this.api.updatePlan(e.id, body) : this.api.createPlan(body))
      .subscribe(() => { this.ok(); this.planOpen.set(false); this.plans.load(); });
  }

  planAction(pl: Plan, action: 'publish' | 'archive' | 'version') {
    const req = action === 'publish' ? this.api.publishPlan(pl.id) : action === 'archive' ? this.api.archivePlan(pl.id) : this.api.newPlanVersion(pl.id);
    req.subscribe(() => { this.ok(); this.plans.load(); this.products.load(); });
  }
}
