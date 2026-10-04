import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { ProblemDetails } from './api.models';
import { I18n } from './i18n.service';
import { Toasts } from './toast.service';

const isAuthCall = (req: HttpRequest<unknown>) => /\/auth\/(login|refresh|logout)$/.test(req.url);

/** Adds the bearer token and the platform admin's tenant scope; on 401 refreshes once and retries. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const withAuth = (r: HttpRequest<unknown>) => {
    const token = auth.token();
    let headers = r.headers;
    if (token) headers = headers.set('Authorization', `Bearer ${token}`);
    const scope = auth.tenantScope();
    if (scope && auth.isPlatformAdmin()) headers = headers.set('X-Tenant-Id', scope);
    return r.clone({ headers });
  };

  return next(withAuth(req)).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status !== 401 || isAuthCall(req)) return throwError(() => error);
      return auth.refresh().pipe(
        switchMap(ok => {
          if (!ok) {
            auth.logout();
            return throwError(() => error);
          }
          return next(withAuth(req));
        }),
      );
    }),
  );
};

/** Shows a toast for every failed call, using the API's stable error code for a translated message. */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toasts = inject(Toasts);
  const i18n = inject(I18n);
  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status !== 401 || isAuthCall(req)) toasts.error(problemMessage(error, i18n));
      return throwError(() => error);
    }),
  );
};

export function problemMessage(error: HttpErrorResponse, i18n: I18n): string {
  if (error.status === 0) return i18n.t('errors.network');
  const problem = error.error as ProblemDetails | null;
  const code = problem?.code;
  if (code && i18n.has(`codes.${code}`)) return i18n.t(`codes.${code}`);
  if (problem?.errors) return Object.values(problem.errors).flat().join(' · ');
  return problem?.title ?? i18n.t('errors.unexpected');
}

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isLoggedIn() ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Route-level permission check. The API enforces the same permission; this only avoids showing dead screens. */
export const permissionGuard = (...permissions: string[]): CanActivateFn => () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.canAny(...permissions) ? true : router.createUrlTree(['/forbidden']);
};
