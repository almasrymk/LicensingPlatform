import { Component, computed, input } from '@angular/core';

export interface Series { name: string; color: string; values: number[]; }

/**
 * Two-series line chart with area fill (mockup: Revenue & Subscriptions Trend). The first series sets the left axis;
 * the second is scaled onto the same height so both trends read together.
 */
@Component({
  selector: 'app-line-chart',
  template: `
    <svg class="line-chart" [attr.viewBox]="'0 0 ' + W + ' ' + H" preserveAspectRatio="none" role="img" [attr.aria-label]="label()">
      @for (t of ticks(); track $index) {
        <line [attr.x1]="L" [attr.x2]="W - R" [attr.y1]="t.y" [attr.y2]="t.y" stroke="#eef2f7" />
        <text [attr.x]="L - 8" [attr.y]="t.y + 4" text-anchor="end">{{ t.label }}</text>
      }
      @for (s of paths(); track s.name) {
        <path [attr.d]="s.area" [attr.fill]="s.color" fill-opacity="0.08" />
        <path [attr.d]="s.line" fill="none" [attr.stroke]="s.color" stroke-width="2.5" stroke-linejoin="round" />
        @for (p of s.points; track $index) {
          <circle [attr.cx]="p.x" [attr.cy]="p.y" r="3.5" fill="#fff" [attr.stroke]="s.color" stroke-width="2"><title>{{ p.title }}</title></circle>
        }
      }
      @for (l of xLabels(); track $index) {
        <text [attr.x]="l.x" [attr.y]="H - 6" text-anchor="middle">{{ l.text }}</text>
      }
    </svg>
  `,
})
export class LineChart {
  readonly series = input.required<Series[]>();
  readonly labels = input.required<string[]>();
  readonly label = input('');
  readonly format = input<(v: number) => string>(v => String(v));

  readonly W = 640; readonly H = 250; readonly L = 52; readonly R = 12; readonly T = 12; readonly B = 28;

  private readonly max = computed(() => {
    const first = this.series()[0]?.values ?? [];
    const m = Math.max(1, ...first);
    const step = Math.pow(10, Math.floor(Math.log10(m)));
    return Math.ceil(m / step) * step;
  });

  readonly ticks = computed(() => Array.from({ length: 6 }, (_, i) => {
    const v = (this.max() / 5) * i;
    return { y: this.yFor(v / this.max()), label: this.format()(v) };
  }));

  readonly xLabels = computed(() => this.labels().map((text, i) => ({ text, x: this.xFor(i) })));

  readonly paths = computed(() => this.series().map((s, si) => {
    const own = Math.max(1, ...s.values);
    const scale = si === 0 ? this.max() : own * 1.25;
    const points = s.values.map((v, i) => ({ x: this.xFor(i), y: this.yFor(v / scale), title: `${s.name}: ${si === 0 ? this.format()(v) : v}` }));
    const line = points.map((p, i) => `${i ? 'L' : 'M'}${p.x},${p.y}`).join(' ');
    const base = this.yFor(0);
    const area = points.length ? `${line} L${points[points.length - 1].x},${base} L${points[0].x},${base} Z` : '';
    return { name: s.name, color: s.color, points, line, area };
  }));

  private xFor(i: number) {
    const n = Math.max(1, this.labels().length - 1);
    return this.L + ((this.W - this.L - this.R) * i) / n;
  }

  private yFor(ratio: number) { return this.T + (this.H - this.T - this.B) * (1 - Math.min(1, Math.max(0, ratio))); }
}

export interface Slice { label: string; value: number; color: string; }

/** Donut with a centered total (mockup: Licenses by Product, Subscription Status). */
@Component({
  selector: 'app-donut',
  template: `
    <div class="donut" [style.width.px]="size()" [style.height.px]="size()">
      <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 120 120">
        <circle cx="60" cy="60" r="46" fill="none" stroke="#eef2f7" stroke-width="16" />
        @for (a of arcs(); track $index) {
          <circle cx="60" cy="60" r="46" fill="none" [attr.stroke]="a.color" stroke-width="16"
            [attr.stroke-dasharray]="a.dash" [attr.stroke-dashoffset]="a.offset"><title>{{ a.label }}: {{ a.value }}</title></circle>
        }
      </svg>
      <div class="center"><strong>{{ centerValue() }}</strong><small>{{ centerLabel() }}</small></div>
    </div>
  `,
})
export class Donut {
  readonly slices = input.required<Slice[]>();
  readonly centerValue = input<string | number>('');
  readonly centerLabel = input('');
  readonly size = input(190);

  readonly arcs = computed(() => {
    const total = this.slices().reduce((s, x) => s + x.value, 0) || 1;
    const c = 2 * Math.PI * 46;
    let acc = 0;
    return this.slices().filter(s => s.value > 0).map(s => {
      const len = (s.value / total) * c;
      const arc = { ...s, dash: `${Math.max(0, len - 1.5)} ${c}`, offset: -acc };
      acc += len;
      return arc;
    });
  });
}

/** Mockup palette for categorical series. */
export const CHART_COLORS = ['#2563eb', '#f59e0b', '#1e40af', '#fb923c', '#10b981', '#8b5cf6', '#ef4444', '#0ea5e9'];
