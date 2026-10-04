import { Component, DestroyRef, booleanAttribute, computed, inject, input, model, output, signal } from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { I18n } from '../core/i18n.service';

/** Loading / error / data state for one request, with reload. Cancels the previous request when reloaded. */
export class Loader<T> {
  readonly data = signal<T | null>(null);
  readonly loading = signal(false);
  readonly error = signal(false);
  private sub?: Subscription;

  constructor(private source: () => Observable<T>, destroyRef?: DestroyRef) {
    destroyRef?.onDestroy(() => this.sub?.unsubscribe());
  }

  load(): void {
    this.sub?.unsubscribe();
    this.loading.set(true);
    this.error.set(false);
    this.sub = this.source().subscribe({
      next: v => { this.data.set(v); this.loading.set(false); },
      error: () => { this.error.set(true); this.loading.set(false); },
    });
  }
}

/** Creates a loader bound to the component's lifetime. Pass autoLoad=false when an effect drives loading (filters, paging). */
export function loader<T>(source: () => Observable<T>, autoLoad = true): Loader<T> {
  const l = new Loader(source, inject(DestroyRef));
  if (autoLoad) l.load();
  return l;
}

/** Runs a mutation, shows a success toast and calls back. Errors are already toasted by the interceptor. */
export function runAction<T>(obs: Observable<T>, done?: (value: T) => void, successKey = 'common.saved') {
  const toasts = inject(Toasts);
  const i18n = inject(I18n);
  return () => obs.subscribe({ next: v => { toasts.success(i18n.t(successKey)); done?.(v); } });
}

@Component({
  selector: 'app-status',
  imports: [TranslatePipe],
  template: `<span class="badge" [class]="'badge ' + tone()">{{ 'status.' + value() | t }}</span>`,
})
export class StatusBadge {
  readonly value = input.required<string>();
  readonly tone = computed(() => {
    switch (this.value()) {
      // One color per status across the whole portal (design system).
      case 'Active': case 'Published': return 'ok';
      case 'Trial': case 'Disabled': return 'warn';
      case 'Suspended': case 'Revoked': case 'Cancelled': case 'Expired': return 'bad';
      default: return 'neutral';
    }
  });
}

/** Page title with its English name (as in the design), subtitle, back link, and the primary actions at the end. */
@Component({
  selector: 'app-page-head',
  imports: [TranslatePipe, RouterLink],
  template: `
    <header class="page-head">
      <div>
        @if (back()) { <nav class="crumbs"><a [routerLink]="back()">{{ 'common.back' | t }}</a></nav> }
        <h1>
          {{ title() | t }}
          @if (i18n.lang() === 'ar' && en()) { <span class="title-en" dir="ltr">({{ en() }})</span> }
          <ng-content select="[badge]" />
        </h1>
        @if (subtitle()) { <p class="subtitle">{{ subtitle() | t }}</p> }
        <ng-content select="[sub]" />
      </div>
      <div class="actions"><ng-content /></div>
    </header>
  `,
})
export class PageHead {
  readonly i18n = inject(I18n);
  readonly title = input.required<string>();
  readonly en = input('');
  readonly subtitle = input('');
  readonly back = input<string | null>(null);
}

@Component({
  selector: 'app-state',
  imports: [TranslatePipe],
  template: `
    @if (loading()) {
      <div class="state" role="status"><span class="spinner" aria-hidden="true"></span>{{ 'common.loading' | t }}</div>
    } @else if (error()) {
      <div class="state state-error" role="alert">
        {{ 'common.error' | t }}
        <button class="btn btn-ghost" type="button" (click)="retry.emit()">{{ 'common.retry' | t }}</button>
      </div>
    } @else if (empty()) {
      <div class="state">{{ 'common.empty' | t }}</div>
    } @else {
      <ng-content />
    }
  `,
})
export class StateView {
  readonly loading = input(false);
  readonly error = input(false);
  readonly empty = input(false);
  readonly retry = output<void>();
}

@Component({
  selector: 'app-pager',
  imports: [TranslatePipe],
  template: `
    @if (total() > 0) {
      <div class="table-foot">
        <span>{{ 'pager.showing' | t: { from: from(), to: to(), total: total() } }}</span>
        <nav class="pages" aria-label="pagination">
          <button type="button" [attr.aria-label]="'common.prev' | t" [disabled]="page() <= 1" (click)="go(page() - 1)">‹</button>
          @for (p of numbers(); track $index) {
            @if (p === 0) { <span class="gap">…</span> }
            @else { <button type="button" [class.active]="p === page()" (click)="go(p)">{{ p }}</button> }
          }
          <button type="button" [attr.aria-label]="'common.next' | t" [disabled]="page() >= pages()" (click)="go(page() + 1)">›</button>
          @if (sizes().length) {
            <select [value]="pageSize()" (change)="setSize(+$any($event.target).value)" [attr.aria-label]="'pager.perPage' | t: { n: pageSize() }">
              @for (s of sizes(); track s) { <option [value]="s" [selected]="s === pageSize()">{{ 'pager.perPage' | t: { n: s } }}</option> }
            </select>
          }
        </nav>
      </div>
    }
  `,
})
export class Pager {
  readonly page = model(1);
  readonly total = input(0);
  readonly pageSize = model(20);
  readonly sizes = input<number[]>([10, 20, 50]);
  readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  readonly from = computed(() => Math.min(this.total(), (this.page() - 1) * this.pageSize() + 1));
  readonly to = computed(() => Math.min(this.total(), this.page() * this.pageSize()));
  /** Page numbers with 0 as an ellipsis: 1 … 4 5 6 … 26. */
  readonly numbers = computed(() => {
    const n = this.pages(), p = this.page();
    if (n <= 7) return Array.from({ length: n }, (_, i) => i + 1);
    const set = [1, p - 1, p, p + 1, n].filter(x => x >= 1 && x <= n);
    const sorted = [...new Set(set)].sort((a, b) => a - b);
    const out: number[] = [];
    sorted.forEach((x, i) => { if (i && x - sorted[i - 1] > 1) out.push(0); out.push(x); });
    return out;
  });
  go(p: number) { this.page.set(Math.min(Math.max(1, p), this.pages())); }
  setSize(size: number) { this.pageSize.set(size); this.page.set(1); }
}

@Component({
  selector: 'app-modal',
  imports: [TranslatePipe],
  template: `
    @if (open()) {
      <div class="modal-backdrop" (click)="close()"></div>
      <div class="modal" role="dialog" aria-modal="true" [attr.aria-label]="title()" [class.modal-wide]="wide()">
        <header class="modal-head">
          <h2>{{ title() }}</h2>
          <button class="btn btn-ghost icon" type="button" [attr.aria-label]="'common.close' | t" (click)="close()">✕</button>
        </header>
        <div class="modal-body"><ng-content /></div>
      </div>
    }
  `,
})
export class Modal {
  readonly open = model(false);
  readonly title = input('');
  readonly wide = input(false, { transform: booleanAttribute });
  close() { this.open.set(false); }
}

/** One-time secret display (product key / client secret) with copy button. */
@Component({
  selector: 'app-secret',
  imports: [TranslatePipe],
  template: `
    <div class="secret">
      <strong>{{ title() }}</strong>
      <p class="muted">{{ hint() }}</p>
      <div class="secret-row">
        <code dir="ltr">{{ value() }}</code>
        <button class="btn" type="button" (click)="copy()">{{ (copied() ? 'common.copied' : 'common.copy') | t }}</button>
      </div>
    </div>
  `,
})
export class SecretBox {
  readonly title = input('');
  readonly hint = input('');
  readonly value = input.required<string>();
  readonly copied = signal(false);
  copy() {
    navigator.clipboard?.writeText(this.value()).then(() => this.copied.set(true), () => undefined);
  }
}
