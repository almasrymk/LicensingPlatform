import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { provideZonelessChangeDetection } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth.service';
import { authGuard, authInterceptor, permissionGuard, problemMessage } from './http';
import { I18n } from './i18n.service';
import { AuthResponse } from './api.models';
import { HttpErrorResponse } from '@angular/common/http';

function response(role: AuthResponse['user']['role'], permissions: string[]): AuthResponse {
  return {
    accessToken: 'access-1', refreshToken: 'refresh-1', expiresAt: new Date(Date.now() + 60_000).toISOString(),
    user: { id: 'u1', email: 'a@b.test', fullName: 'A', role, permissions, language: 'ar' },
  };
}

describe('core', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => backend.verify());

  async function signIn(role: AuthResponse['user']['role'], permissions: string[]) {
    const login = firstValueFrom(auth.login('a@b.test', 'x'));
    backend.expectOne('/api/v1/auth/login').flush(response(role, permissions));
    await login;
  }

  it('permission checks follow the profile', async () => {
    await signIn('TenantOperator', ['customers.read', 'licenses.read']);
    expect(auth.can('customers.read')).toBe(true);
    expect(auth.can('users.manage')).toBe(false);
    expect(auth.canAny('users.manage', 'licenses.read')).toBe(true);
  });

  it('interceptor adds the bearer token', async () => {
    await signIn('TenantAdmin', []);
    http.get('/api/v1/customers').subscribe();
    const req = backend.expectOne('/api/v1/customers');
    expect(req.request.headers.get('Authorization')).toBe('Bearer access-1');
    expect(req.request.headers.has('X-Tenant-Id')).toBe(false);
    req.flush({});
  });

  it('only platform admins send the tenant scope header', async () => {
    await signIn('PlatformAdmin', []);
    auth.setTenantScope('tenant-42');
    http.get('/api/v1/customers').subscribe();
    const req = backend.expectOne('/api/v1/customers');
    expect(req.request.headers.get('X-Tenant-Id')).toBe('tenant-42');
    req.flush({});
  });

  it('a 401 triggers one refresh and the request is retried with the new token', async () => {
    await signIn('TenantAdmin', []);
    const result = firstValueFrom(http.get<{ ok: boolean }>('/api/v1/licenses'));
    backend.expectOne('/api/v1/licenses').flush(null, { status: 401, statusText: 'Unauthorized' });
    const refreshed = response('TenantAdmin', []);
    refreshed.accessToken = 'access-2';
    backend.expectOne('/api/v1/auth/refresh').flush(refreshed);
    const retry = backend.expectOne('/api/v1/licenses');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer access-2');
    retry.flush({ ok: true });
    expect(await result).toEqual({ ok: true });
  });

  it('guards redirect anonymous users to login and users without permission to forbidden', async () => {
    const router = TestBed.inject(Router);
    const anonymous = TestBed.runInInjectionContext(() => authGuard({} as never, { url: '/licenses' } as never)) as UrlTree;
    expect(router.serializeUrl(anonymous)).toBe('/login?returnUrl=%2Flicenses');

    await signIn('CustomerUser', ['licenses.read']);
    expect(TestBed.runInInjectionContext(() => permissionGuard('licenses.read')({} as never, {} as never))).toBe(true);
    const denied = TestBed.runInInjectionContext(() => permissionGuard('users.manage')({} as never, {} as never)) as UrlTree;
    expect(router.serializeUrl(denied)).toBe('/forbidden');
  });

  it('i18n switches direction and translates API error codes', () => {
    const i18n = TestBed.inject(I18n);
    i18n.set('ar');
    TestBed.tick();
    expect(document.documentElement.dir).toBe('rtl');
    const error = new HttpErrorResponse({ status: 409, error: { code: 'DUPLICATE', title: 'x' } });
    expect(problemMessage(error, i18n)).toBe('العنصر موجود بالفعل.');
    i18n.set('en');
    TestBed.tick();
    expect(document.documentElement.dir).toBe('ltr');
    expect(i18n.t('common.days', { n: 7 })).toBe('7 days');
  });
});
