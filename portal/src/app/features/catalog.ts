import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Platforms, Product } from '../core/api.models';
import { I18n, LocalNumberPipe, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Icon } from '../shared/icon';
import { Modal, StateView, loader } from '../shared/ui';

/** Products from the mockup: cards with icon tile, platform chips, plans/licenses counts, status and a row menu. */
@Component({
  selector: 'app-catalog',
  imports: [FormsModule, TranslatePipe, LocalNumberPipe, StateView, Modal, Icon],
  template: `
    <header class="page-head">
      <div><h1>{{ 'products.title' | t }}</h1><p class="subtitle">{{ 'products.sub' | t }}</p></div>
      @if (canManage()) {
        <button class="btn btn-primary" type="button" (click)="newProduct()"><app-icon name="plus" [size]="16" />{{ 'products.new' | t }}</button>
      }
    </header>

    <div class="filters">
      <div class="search">
        <app-icon name="search" [size]="16" />
        <input type="search" [placeholder]="'products.searchPh' | t" [ngModel]="search()" (ngModelChange)="search.set($event)" />
      </div>
      <select [ngModel]="status()" (ngModelChange)="status.set($event)" [attr.aria-label]="'col.status' | t">
        <option value="">{{ 'filter.allStatus' | t }}</option>
        <option value="active">{{ 'status.Active' | t }}</option>
        <option value="inactive">{{ 'status.Inactive' | t }}</option>
      </select>
    </div>

    <app-state [loading]="products.loading() && !products.data()" [error]="products.error()" [empty]="visible().length === 0" (retry)="products.load()">
      <div class="product-grid2">
        @for (p of visible(); track p.id; let i = $index) {
          <article class="pcard">
            <div class="pcard-head">
              <span class="tile" [class]="'tile ' + tone(i)"><app-icon name="cube" [size]="22" /></span>
              <div><strong>{{ p.name }}</strong><small>{{ p.description }}</small></div>
              <div class="row-menu">
                <button class="kebab" type="button" [attr.aria-label]="'col.actions' | t" (click)="toggleMenu(p.id, $event)"><app-icon name="moreV" /></button>
                @if (menuFor() === p.id) {
                  <div class="menu" role="menu">
                    <button role="menuitem" type="button" (click)="router.navigate(['/plans'], { queryParams: { product: p.id } })"><app-icon name="layers" [size]="16" />{{ 'products.viewPlans' | t }}</button>
                    @if (canManage()) {
                      <button role="menuitem" type="button" (click)="editProduct(p)"><app-icon name="edit" [size]="16" />{{ 'common.edit' | t }}</button>
                      <button role="menuitem" type="button" (click)="toggleActive(p)"><app-icon name="ban" [size]="16" />{{ (p.isActive ? 'customers.deactivate' : 'customers.activate') | t }}</button>
                    }
                  </div>
                }
              </div>
            </div>
            <div class="platform-chips">@for (pl of p.platforms; track pl) { <span>{{ pl }}</span> }</div>
            <div class="pcard-stats">
              <div><small>{{ 'nav.plans' | t }}</small><b>{{ p.plans | num }}</b></div>
              <div><small>{{ 'col.licenses' | t }}</small><b>{{ p.licenses | num }}</b></div>
              <span class="badge" [class.ok]="p.isActive" [class.neutral]="!p.isActive">{{ (p.isActive ? 'status.Active' : 'status.Inactive') | t }}</span>
            </div>
          </article>
        }
      </div>
    </app-state>

    <app-modal [(open)]="productOpen" [title]="(editingProduct() ? 'common.edit' : 'products.new') | t">
      <form class="form" (ngSubmit)="saveProduct()">
        <label class="field"><span>{{ 'common.code' | t }}</span><input name="code" dir="ltr" required [disabled]="!!editingProduct()" [(ngModel)]="pForm.code" /></label>
        <label class="field"><span>{{ 'common.name' | t }}</span><input name="name" required [(ngModel)]="pForm.name" /></label>
        <label class="field"><span>{{ 'common.description' | t }}</span><textarea name="desc" rows="3" [(ngModel)]="pForm.description"></textarea></label>
        <fieldset class="field">
          <legend>{{ 'products.platforms' | t }}</legend>
          <div class="platform-chips">
            @for (pl of allPlatforms; track pl) {
              <label class="check"><input type="checkbox" [checked]="pForm.platforms.includes(pl)" (change)="togglePlatform(pl)" /> {{ pl }}</label>
            }
          </div>
        </fieldset>
        @if (editingProduct()) { <label class="check"><input type="checkbox" name="active" [(ngModel)]="pForm.isActive" /> {{ 'common.active' | t }}</label> }
        <div class="form-actions"><button class="btn btn-primary" type="submit">{{ 'common.save' | t }}</button></div>
      </form>
    </app-modal>
  `,
})
export class CatalogPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  readonly i18n = inject(I18n);
  readonly router = inject(Router);
  readonly auth = inject(AuthService);
  readonly canManage = computed(() => this.auth.can(this.auth.perm.CatalogManage));
  readonly allPlatforms = Platforms;

  readonly search = signal('');
  readonly status = signal('');
  readonly menuFor = signal<string | null>(null);
  readonly products = loader(() => this.api.products({ pageSize: 200 }));
  readonly visible = computed(() => {
    const q = this.search().trim().toLowerCase();
    return (this.products.data()?.items ?? []).filter(p =>
      (!q || p.name.toLowerCase().includes(q) || p.code.toLowerCase().includes(q)) &&
      (!this.status() || (this.status() === 'active') === p.isActive));
  });

  readonly productOpen = signal(false);
  readonly editingProduct = signal<Product | null>(null);
  pForm = { code: '', name: '', description: '', isActive: true, platforms: [] as string[] };

  @HostListener('document:click') closeMenus() { this.menuFor.set(null); }
  toggleMenu(id: string, e: Event) { e.stopPropagation(); this.menuFor.set(this.menuFor() === id ? null : id); }
  tone(i: number) { return ['blue', 'violet', 'teal', 'orange'][i % 4]; }

  togglePlatform(pl: string) {
    this.pForm.platforms = this.pForm.platforms.includes(pl) ? this.pForm.platforms.filter(x => x !== pl) : [...this.pForm.platforms, pl];
  }

  private ok() { this.toasts.success(this.i18n.t('common.saved')); }

  newProduct() { this.editingProduct.set(null); this.pForm = { code: '', name: '', description: '', isActive: true, platforms: ['Windows'] }; this.productOpen.set(true); }

  editProduct(p: Product) {
    this.editingProduct.set(p);
    this.pForm = { code: p.code, name: p.name, description: p.description ?? '', isActive: p.isActive, platforms: [...p.platforms] };
    this.productOpen.set(true);
  }

  toggleActive(p: Product) {
    this.api.updateProduct(p.id, { code: p.code, name: p.name, description: p.description, isActive: !p.isActive, platforms: p.platforms })
      .subscribe(() => { this.ok(); this.products.load(); });
  }

  saveProduct() {
    const e = this.editingProduct();
    const req = e ? this.api.updateProduct(e.id, this.pForm) : this.api.createProduct(this.pForm);
    req.subscribe(() => { this.ok(); this.productOpen.set(false); this.products.load(); });
  }
}
