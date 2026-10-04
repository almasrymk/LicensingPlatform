import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, finalize, map, of, shareReplay, tap, catchError } from 'rxjs';
import { Api } from './api.service';
import { AuthResponse, Perm, UserProfile } from './api.models';

const REFRESH_KEY = 'lic.refresh';
const TENANT_KEY = 'lic.tenantScope';

/**
 * Session state. The access token lives in memory only; the rotating refresh token is kept in sessionStorage so a reload
 * keeps the session but closing the tab ends it.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private api = inject(Api);
  private router = inject(Router);

  private accessToken = signal<string | null>(null);
  readonly user = signal<UserProfile | null>(null);
  readonly isLoggedIn = computed(() => this.user() !== null);
  readonly isPlatformAdmin = computed(() => this.user()?.role === 'PlatformAdmin');
  readonly isCustomerUser = computed(() => this.user()?.role === 'CustomerUser');

  /** Platform admins can narrow every screen to one tenant (sent as X-Tenant-Id). */
  readonly tenantScope = signal<string | null>(this.read(TENANT_KEY));

  private refreshing$: Observable<boolean> | null = null;

  token(): string | null { return this.accessToken(); }

  can(permission: string): boolean { return this.user()?.permissions.includes(permission) ?? false; }
  canAny(...permissions: string[]): boolean { return permissions.some(p => this.can(p)); }
  readonly perm = Perm;

  login(email: string, password: string) {
    return this.api.login(email, password).pipe(tap(r => this.apply(r)));
  }

  /** Restores the session on startup from the stored refresh token. */
  restore(): Observable<boolean> {
    return this.read(REFRESH_KEY) ? this.refresh() : of(false);
  }

  /** Single-flight refresh: concurrent 401s wait for the same refresh call. */
  refresh(): Observable<boolean> {
    const token = this.read(REFRESH_KEY);
    if (!token) return of(false);
    this.refreshing$ ??= this.api.refresh(token).pipe(
      tap(r => this.apply(r)),
      map(() => true),
      catchError(() => { this.clear(); return of(false); }),
      finalize(() => (this.refreshing$ = null)),
      shareReplay(1),
    );
    return this.refreshing$;
  }

  logout(): void {
    const token = this.read(REFRESH_KEY);
    if (token) this.api.logout(token).subscribe({ error: () => undefined });
    this.clear();
    this.router.navigateByUrl('/login');
  }

  setTenantScope(tenantId: string | null): void {
    this.tenantScope.set(tenantId);
    try { tenantId ? sessionStorage.setItem(TENANT_KEY, tenantId) : sessionStorage.removeItem(TENANT_KEY); } catch { /* storage unavailable */ }
  }

  updateProfile(patch: Partial<UserProfile>): void {
    const u = this.user();
    if (u) this.user.set({ ...u, ...patch });
  }

  private apply(r: AuthResponse): void {
    this.accessToken.set(r.accessToken);
    this.user.set(r.user);
    try { sessionStorage.setItem(REFRESH_KEY, r.refreshToken); } catch { /* storage unavailable */ }
  }

  clear(): void {
    this.accessToken.set(null);
    this.user.set(null);
    this.setTenantScope(null);
    try { sessionStorage.removeItem(REFRESH_KEY); } catch { /* storage unavailable */ }
  }

  private read(key: string): string | null {
    try { return sessionStorage.getItem(key); } catch { return null; }
  }
}
