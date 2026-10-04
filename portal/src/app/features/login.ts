import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { I18n, TranslatePipe } from '../core/i18n.service';
import { Icon } from '../shared/icon';

/** Split-screen login from the mockup: form on one side, blue product panel on the other. */
@Component({
  selector: 'app-login',
  imports: [FormsModule, TranslatePipe, Icon],
  template: `
    <div class="login2">
      <section class="form-side">
        <div class="brand2"><span class="logo-cube"><app-icon name="cube" [size]="34" /></span><strong>Licensing Platform</strong></div>
        <h1>{{ 'login.welcome' | t }}</h1>
        <p class="lead">{{ 'login.lead' | t }}</p>

        <form (ngSubmit)="submit()" #f="ngForm">
          <label class="field">
            <span>{{ 'login.email' | t }}</span>
            <div class="input-icon">
              <app-icon name="user" [size]="16" />
              <input type="email" name="email" autocomplete="username" dir="ltr" placeholder="name@company.com" required [(ngModel)]="email" />
            </div>
          </label>
          <label class="field">
            <span>{{ 'login.password' | t }}</span>
            <div class="input-icon">
              <app-icon name="lock" [size]="16" />
              <input [type]="showPassword() ? 'text' : 'password'" name="password" autocomplete="current-password" dir="ltr" required [(ngModel)]="password" />
              <button type="button" class="icon-btn eye" [attr.aria-label]="'login.showPassword' | t" (click)="showPassword.set(!showPassword())"><app-icon name="eye" [size]="16" /></button>
            </div>
          </label>
          <div class="row">
            <label class="check"><input type="checkbox" name="remember" [(ngModel)]="remember" /> {{ 'login.remember' | t }}</label>
            <button type="button" class="link" (click)="forgot.set(!forgot())">{{ 'login.forgot' | t }}</button>
          </div>
          @if (forgot()) { <p class="hint">{{ 'login.forgotHint' | t }}</p> }
          <button class="btn btn-primary block" type="submit" [disabled]="busy() || f.invalid">
            {{ 'login.signIn' | t }} <app-icon name="arrowRight" class="flip" [size]="18" />
          </button>

          <div class="or">{{ 'login.orContinue' | t }}</div>
          <div class="sso">
            <button class="btn" type="button" disabled [title]="'login.ssoSoon' | t">
              <span class="ms-logo" aria-hidden="true"><i style="background:#f25022"></i><i style="background:#7fba00"></i><i style="background:#00a4ef"></i><i style="background:#ffb900"></i></span>Microsoft
            </button>
            <button class="btn" type="button" disabled [title]="'login.ssoSoon' | t"><span class="g-logo" aria-hidden="true">G</span>Google</button>
          </div>
        </form>

        <p class="contact">{{ 'login.noAccount' | t }} <a href="mailto:admin@licensing.local">{{ 'login.contactAdmin' | t }}</a></p>

        <details class="demo">
          <summary>{{ 'login.demo' | t }}</summary>
          <ul>
            @for (d of demo; track d.email) {
              <li><button type="button" class="link" (click)="fill(d.email, d.password)"><span dir="ltr">{{ d.email }}</span></button> — {{ 'role.' + d.role | t }}</li>
            }
          </ul>
        </details>

        <nav class="foot">
          <span>{{ 'login.privacy' | t }}</span><span>{{ 'login.terms' | t }}</span><span>{{ 'login.help' | t }}</span>
          <button type="button" class="link" (click)="i18n.toggle()">{{ 'nav.language' | t }}</button>
        </nav>
      </section>

      <section class="promo" aria-hidden="true">
        <h2>{{ 'promo.title' | t }}</h2>
        <p>{{ 'promo.lead' | t }}</p>
        <ul>
          <li><span class="tile"><app-icon name="users" /></span><div><strong>{{ 'promo.f1' | t }}</strong><small>{{ 'promo.f1s' | t }}</small></div></li>
          <li><span class="tile"><app-icon name="key" /></span><div><strong>{{ 'promo.f2' | t }}</strong><small>{{ 'promo.f2s' | t }}</small></div></li>
          <li><span class="tile"><app-icon name="shield" /></span><div><strong>{{ 'promo.f3' | t }}</strong><small>{{ 'promo.f3s' | t }}</small></div></li>
          <li><span class="tile"><app-icon name="code" /></span><div><strong>{{ 'promo.f4' | t }}</strong><small>{{ 'promo.f4s' | t }}</small></div></li>
        </ul>
        <!-- Artwork taken from the design board. -->
        <img class="art" src="img/login-illustration.png" alt="" />
      </section>
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
  readonly showPassword = signal(false);

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
        this.router.navigateByUrl(this.route.snapshot.queryParamMap.get('returnUrl') ?? '/');
      },
      error: () => this.busy.set(false),
    });
  }
}
