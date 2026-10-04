import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { I18n, Lang, TranslatePipe } from '../core/i18n.service';
import { Toasts } from '../core/toast.service';
import { PageHead } from '../shared/ui';
import { ImageUpload } from '../shared/media';

@Component({
  selector: 'app-settings',
  imports: [FormsModule, TranslatePipe, PageHead, ImageUpload],
  template: `
    <app-page-head title="settings.title" en="Settings" subtitle="settings.sub">
    </app-page-head>
    <div class="grid-2">
      <section class="card">
        <h2>{{ 'settings.profile' | t }}</h2>
        <div class="field"><span>{{ 'img.photo' | t }}</span>
          <app-image-upload owner="me" [url]="auth.user()?.imageUrl" [name]="auth.user()?.fullName ?? ''" [round]="true" (changed)="auth.updateProfile({ imageUrl: $event })" />
        </div>
        <dl class="dl">
          <dt>{{ 'users.fullName' | t }}</dt><dd>{{ auth.user()?.fullName }}</dd>
          <dt>{{ 'common.email' | t }}</dt><dd dir="ltr">{{ auth.user()?.email }}</dd>
          <dt>{{ 'users.role' | t }}</dt><dd>{{ 'role.' + auth.user()?.role | t }}</dd>
          @if (auth.user()?.tenantName) { <dt>{{ 'common.tenant' | t }}</dt><dd>{{ auth.user()?.tenantName }}</dd> }
          @if (auth.user()?.customerName) { <dt>{{ 'common.customer' | t }}</dt><dd>{{ auth.user()?.customerName }}</dd> }
        </dl>
        <h3>{{ 'settings.language' | t }}</h3>
        <div class="segmented">
          <button type="button" [class.active]="i18n.lang() === 'ar'" (click)="setLang('ar')">العربية</button>
          <button type="button" [class.active]="i18n.lang() === 'en'" (click)="setLang('en')">English</button>
        </div>
        <h3>{{ 'settings.permissions' | t }}</h3>
        <div class="chips">@for (p of auth.user()?.permissions; track p) { <span class="chip" dir="ltr">{{ p }}</span> }</div>
      </section>

      <section class="card">
        <h2>{{ 'settings.password' | t }}</h2>
        <form class="form" (ngSubmit)="changePassword()">
          <label class="field"><span>{{ 'settings.current' | t }}</span><input name="cur" type="password" dir="ltr" autocomplete="current-password" required [(ngModel)]="current" /></label>
          <label class="field"><span>{{ 'settings.new' | t }}</span><input name="new" type="password" dir="ltr" autocomplete="new-password" required [(ngModel)]="next" /></label>
          <div class="form-actions"><button class="btn btn-primary" type="submit" [disabled]="!current || !next">{{ 'common.save' | t }}</button></div>
        </form>
      </section>
    </div>
  `,
})
export class SettingsPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  readonly auth = inject(AuthService);
  readonly i18n = inject(I18n);
  current = '';
  next = '';

  setLang(lang: Lang) {
    this.i18n.set(lang);
    this.auth.updateProfile({ language: lang });
    this.api.setLanguage(lang).subscribe();
  }

  changePassword() {
    this.api.changePassword(this.current, this.next).subscribe(() => {
      this.toasts.success(this.i18n.t('settings.changed'));
      this.auth.logout();
    });
  }
}
