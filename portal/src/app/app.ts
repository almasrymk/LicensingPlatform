import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Toasts } from './core/toast.service';
import { TranslatePipe } from './core/i18n.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, TranslatePipe],
  template: `
    <router-outlet />
    <div class="toasts" aria-live="polite">
      @for (toast of toasts.items(); track toast.id) {
        <div class="toast" [class]="'toast toast-' + toast.kind" role="status">
          <span>{{ toast.text }}</span>
          <button type="button" class="btn btn-ghost icon" [attr.aria-label]="'common.close' | t" (click)="toasts.dismiss(toast.id)">✕</button>
        </div>
      }
    </div>
  `,
})
export class App {
  readonly toasts = inject(Toasts);
}
