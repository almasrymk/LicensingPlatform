import { Component, Injectable, computed, inject, input, output, signal } from '@angular/core';
import { Observable, catchError, map, of, shareReplay } from 'rxjs';
import { toSignal } from '@angular/core/rxjs-interop';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { ImageOwner, Plan, Product, Tenant } from '../core/api.models';
import { I18n, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { Icon } from './icon';

/** Picture or fallback (initial letter, or an icon for products) in a rounded tile. */
@Component({
  selector: 'app-avatar',
  imports: [Icon],
  host: { class: 'avatar-box', '[style.width.px]': 'size()', '[style.height.px]': 'size()', '[class.round]': 'round()' },
  template: `
    @if (src()) {
      <img [src]="src()" [alt]="name()" loading="lazy" />
    } @else if (icon()) {
      <span class="tile" [class]="'tile ' + tone()" [style.width.px]="size()" [style.height.px]="size()"><app-icon [name]="icon()!" [size]="size() * 0.5" /></span>
    } @else {
      <span class="initial" [class]="'initial ' + tone()">{{ initial() }}</span>
    }
  `,
})
export class Avatar {
  readonly src = input<string | null | undefined>('');
  readonly name = input('');
  readonly icon = input<string | null | undefined>(null);
  readonly size = input(36);
  readonly round = input(false);
  readonly tone = input('blue');
  readonly initial = computed(() => (this.name() || '?').trim().charAt(0).toUpperCase());
}

/** Image picker for forms: preview, choose, upload immediately (record must exist), remove. */
@Component({
  selector: 'app-image-upload',
  imports: [TranslatePipe, Avatar, Icon],
  template: `
    <div class="image-upload">
      <app-avatar [src]="current()" [name]="name()" [icon]="icon()" [size]="64" [round]="round()" />
      <div class="image-upload-actions">
        <label class="btn btn-sm">
          <app-icon name="download" [size]="14" style="transform:rotate(180deg)" />{{ 'img.choose' | t }}
          <input type="file" accept="image/png,image/jpeg,image/gif,image/webp" hidden (change)="pick($any($event.target))" [disabled]="busy()" />
        </label>
        @if (current()) {
          <button class="btn btn-sm btn-ghost" type="button" (click)="remove()" [disabled]="busy()">{{ 'img.remove' | t }}</button>
        }
        <small class="muted">{{ 'img.hint' | t }}</small>
      </div>
    </div>
  `,
})
export class ImageUpload {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private i18n = inject(I18n);

  readonly owner = input.required<ImageOwner | 'me'>();
  readonly ownerId = input<string>('');
  readonly url = input<string | null | undefined>('');
  readonly name = input('');
  readonly icon = input<string | null | undefined>(null);
  readonly round = input(false);
  readonly changed = output<string>();

  private readonly override = signal<string | null>(null);
  readonly current = computed(() => this.override() ?? this.url() ?? '');
  readonly busy = signal(false);

  pick(el: HTMLInputElement) {
    const file = el.files?.[0];
    el.value = '';
    if (!file) return;
    if (file.size > 2 * 1024 * 1024) { this.toasts.error(this.i18n.t('codes.IMAGE_TOO_LARGE')); return; }
    this.busy.set(true);
    const req = this.owner() === 'me' ? this.api.uploadMyImage(file) : this.api.uploadImage(this.owner() as ImageOwner, this.ownerId(), file);
    req.subscribe({
      next: r => { this.override.set(r.url); this.busy.set(false); this.changed.emit(r.url); this.toasts.success(this.i18n.t('common.saved')); },
      error: () => this.busy.set(false),
    });
  }

  remove() {
    this.busy.set(true);
    const req = this.owner() === 'me' ? this.api.removeMyImage() : this.api.removeImage(this.owner() as ImageOwner, this.ownerId());
    req.subscribe({
      next: () => { this.override.set(''); this.busy.set(false); this.changed.emit(''); },
      error: () => this.busy.set(false),
    });
  }
}

/** Shared option lists for the filter bars, loaded once per session. */
@Injectable({ providedIn: 'root' })
export class Lookups {
  private api = inject(Api);
  private auth = inject(AuthService);
  private cache = new Map<string, Observable<unknown>>();

  private once<T>(key: string, factory: () => Observable<T>): Observable<T> {
    if (!this.cache.has(key)) this.cache.set(key, factory().pipe(catchError(() => of([] as unknown as T)), shareReplay(1)));
    return this.cache.get(key) as Observable<T>;
  }

  tenants(): Observable<Tenant[]> {
    return this.auth.isPlatformAdmin() ? this.once('tenants', () => this.api.tenants({ pageSize: 200 }).pipe(map(p => p.items))) : of([]);
  }
  products(): Observable<Product[]> { return this.once('products', () => this.api.products({ pageSize: 200 }).pipe(map(p => p.items))); }
  plans(): Observable<Plan[]> { return this.once('plans', () => this.api.plans({ pageSize: 200 }).pipe(map(p => p.items))); }
  countries(): Observable<string[]> { return this.once('countries', () => this.api.countries()); }
  invalidate() { this.cache.clear(); }
}

/** Helper for components: signals of the lookup lists. */
export function lookupSignals() {
  const l = inject(Lookups);
  return {
    tenants: toSignal(l.tenants(), { initialValue: [] as Tenant[] }),
    products: toSignal(l.products(), { initialValue: [] as Product[] }),
    plans: toSignal(l.plans(), { initialValue: [] as Plan[] }),
  };
}
