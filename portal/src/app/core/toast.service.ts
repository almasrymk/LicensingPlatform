import { Injectable, signal } from '@angular/core';

export interface Toast { id: number; kind: 'success' | 'error' | 'info'; text: string; }

@Injectable({ providedIn: 'root' })
export class Toasts {
  readonly items = signal<Toast[]>([]);
  private next = 1;

  success(text: string) { this.push('success', text); }
  error(text: string) { this.push('error', text, 7000); }
  info(text: string) { this.push('info', text); }

  dismiss(id: number) { this.items.update(list => list.filter(t => t.id !== id)); }

  private push(kind: Toast['kind'], text: string, ms = 4000) {
    const id = this.next++;
    this.items.update(list => [...list.slice(-3), { id, kind, text }]);
    setTimeout(() => this.dismiss(id), ms);
  }
}
