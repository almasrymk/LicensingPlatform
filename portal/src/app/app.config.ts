import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { routes } from './app.routes';
import { authInterceptor, errorInterceptor } from './core/http';
import { AuthService } from './core/auth.service';
import { I18n } from './core/i18n.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
    // Restore the session (refresh token) before the first navigation, and apply lang/dir immediately.
    provideAppInitializer(() => {
      inject(I18n);
      return firstValueFrom(inject(AuthService).restore()).then(() => undefined);
    }),
  ],
};
