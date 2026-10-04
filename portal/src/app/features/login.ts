import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { I18n, TranslatePipe } from '../core/i18n.service';
import { Icon } from '../shared/icon';

/** Login as in design page 1: deep blue gradient, translucent card, shield mark. */
@Component({
  selector: 'app-login',
  imports: [FormsModule, TranslatePipe, Icon],
  template: `
    <div class="login-page">
      <div>
        <form class="login-card" (ngSubmit)="submit()" #f="ngForm">
          <div class="login-head">
            <span class="brand-mark big"><app-icon name="shield" [size]="28" /></span>
            <h1>{{ 'login.title' | t }}</h1>
            <p>{{ 'login.sub' | t }}</p>
          </div>
          <label class="field">
            <span>{{ 'login.email' | t }}</span>
            <input type="email" name="email" autocomplete="username" dir="ltr" required [(ngModel)]="email" />
          </label>
          <label class="field">
            <span>{{ 'login.password' | t }}</span>
            <input type="password" name="password" autocomplete="current-password" dir="ltr" required [(ngModel)]="password" />
          </label>
          <div class="login-row">
            <label class="check"><input type="checkbox" name="remember" [(ngModel)]="remember" /> {{ 'login.remember' | t }}</label>
            <button type="button" class="link" (click)="forgot.set(!forgot())">{{ 'login.forgot' | t }}</button>
          </div>
          @if (forgot()) { <p class="login-row">{{ 'login.forgotHint' | t }}</p> }
          <button class="btn btn-primary block" type="submit" [disabled]="busy() || f.invalid">{{ 'login.submit' | t }}</button>
          <button class="btn btn-ghost block" type="button" (click)="i18n.toggle()"><app-icon name="globe" />{{ 'nav.language' | t }}</button>

          <details class="demo">
            <summary>{{ 'login.demo' | t }}</summary>
            <ul>
              @for (d of demo; track d.email) {
                <li><button type="button" class="link" (click)="fill(d.email, d.password)"><span dir="ltr">{{ d.email }}</span></button> — {{ 'role.' + d.role | t }}</li>
              }
            </ul>
          </details>
        </form>
        <p class="login-foot" dir="ltr">Licensing Platform · RTL Ready</p>
      </div>
    </div>
  `,
})
export class Login {
  private auth = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  readonly i18n = inject(I18n);

  email = '';
  password = '';
  remember = true;
  readonly busy = signal(false);
  readonly forgot = signal(false);

  readonly demo = [
    { email: 'admin@licensing.local', password: 'Admin@12345', role: 'PlatformAdmin' },
    { email: 'admin@nour.test', password: 'Demo@12345', role: 'TenantAdmin' },
    { email: 'ops@nour.test', password: 'Demo@12345', role: 'TenantOperator' },
    { email: 'user@alamal.test', password: 'Demo@12345', role: 'CustomerUser' },
    { email: 'user@almanar.test', password: 'Demo@12345', role: 'CustomerUser' },
  ];

  fill(email: string, password: string) { this.email = email; this.password = password; }

  submit() {
    this.busy.set(true);
    this.auth.login(this.email, this.password).subscribe({
      next: r => {
        this.i18n.set(r.user.language);
        const target = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/';
        this.router.navigateByUrl(target);
      },
      error: () => this.busy.set(false),
    });
  }
}
