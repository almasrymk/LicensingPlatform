import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../core/i18n.service';

@Component({
  selector: 'app-forbidden',
  imports: [RouterLink, TranslatePipe],
  template: `<div class="empty-page"><h1>403</h1><p>{{ 'common.forbidden' | t }}</p><a class="btn" routerLink="/">{{ 'common.home' | t }}</a></div>`,
})
export class ForbiddenPage {}

@Component({
  selector: 'app-not-found',
  imports: [RouterLink, TranslatePipe],
  template: `<div class="empty-page"><h1>404</h1><p>{{ 'common.notFound' | t }}</p><a class="btn" routerLink="/">{{ 'common.home' | t }}</a></div>`,
})
export class NotFoundPage {}
