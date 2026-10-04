import { Injectable, Pipe, PipeTransform, effect, inject, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { AR, EN } from './i18n.dictionaries';

export type Lang = 'ar' | 'en';
const LANG_KEY = 'lic.lang';

/** Arabic (RTL) by default, English (LTR) on demand. Sets <html lang/dir> so CSS logical properties flip the layout. */
@Injectable({ providedIn: 'root' })
export class I18n {
  private document = inject(DOCUMENT);
  readonly lang = signal<Lang>(this.stored() ?? 'ar');
  readonly dir = () => (this.lang() === 'ar' ? 'rtl' : 'ltr');

  constructor() {
    effect(() => {
      const lang = this.lang();
      this.document.documentElement.lang = lang;
      this.document.documentElement.dir = lang === 'ar' ? 'rtl' : 'ltr';
      try { localStorage.setItem(LANG_KEY, lang); } catch { /* storage unavailable */ }
    });
  }

  set(lang: Lang) { this.lang.set(lang); }
  toggle() { this.lang.update(l => (l === 'ar' ? 'en' : 'ar')); }

  private dict(): Record<string, string> { return this.lang() === 'ar' ? AR : EN; }

  has(key: string): boolean { return key in this.dict(); }

  t(key: string, params?: Record<string, string | number>): string {
    let text = this.dict()[key] ?? EN[key] ?? key;
    if (params) for (const [k, v] of Object.entries(params)) text = text.replaceAll(`{${k}}`, String(v));
    return text;
  }

  date(value?: string | null, withTime = false): string {
    if (!value) return '—';
    const d = new Date(value);
    const locale = this.lang() === 'ar' ? 'ar-EG-u-nu-latn' : 'en-GB';
    return withTime
      ? d.toLocaleString(locale, { dateStyle: 'medium', timeStyle: 'short' })
      : d.toLocaleDateString(locale, { dateStyle: 'medium' });
  }

  number(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    return value.toLocaleString('en-US'); // Latin digits in both languages, as in the design
  }

  private stored(): Lang | null {
    try { const v = localStorage.getItem(LANG_KEY); return v === 'ar' || v === 'en' ? v : null; } catch { return null; }
  }
}

@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private i18n = inject(I18n);
  transform(key: string, params?: Record<string, string | number>): string { return this.i18n.t(key, params); }
}

@Pipe({ name: 'date2', pure: false })
export class LocalDatePipe implements PipeTransform {
  private i18n = inject(I18n);
  transform(value?: string | null, withTime = false): string { return this.i18n.date(value, withTime); }
}

@Pipe({ name: 'num', pure: false })
export class LocalNumberPipe implements PipeTransform {
  private i18n = inject(I18n);
  transform(value: number | null | undefined): string { return this.i18n.number(value); }
}
