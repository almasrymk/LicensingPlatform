import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  ActiveDevice, ApiClient, ApiClientSecret, AuditRecord, AuthResponse, CustomerDetails, Customer, CustomerStatus, Dashboard,
  ExpiringLicense, FailedActivations, Overview, TrendPoint, ActivationRow, IssuedLicense, License, LicenseDetails, Paged, Plan, Product, SavePlan, SigningKeys,
  Subscription, SubscriptionDetails, Tenant, Usage, User, UserProfile,
} from './api.models';

export type Query = Record<string, string | number | boolean | null | undefined>;

function params(q: Query = {}): HttpParams {
  let p = new HttpParams();
  for (const [k, v] of Object.entries(q)) if (v !== null && v !== undefined && v !== '') p = p.set(k, String(v));
  return p;
}

/** Typed client for /api/v1. One method per endpoint; no business logic. */
@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);
  private base = '/api/v1';

  // Auth
  login(email: string, password: string) { return this.http.post<AuthResponse>(`${this.base}/auth/login`, { email, password }); }
  refresh(refreshToken: string) { return this.http.post<AuthResponse>(`${this.base}/auth/refresh`, { refreshToken }); }
  logout(refreshToken: string) { return this.http.post<void>(`${this.base}/auth/logout`, { refreshToken }); }
  me() { return this.http.get<UserProfile>(`${this.base}/auth/me`); }
  setLanguage(language: string) { return this.http.put<void>(`${this.base}/auth/me/language`, { language }); }
  changePassword(currentPassword: string, newPassword: string) {
    return this.http.put<void>(`${this.base}/auth/me/password`, { currentPassword, newPassword });
  }

  // Tenants
  tenants(q?: Query) { return this.http.get<Paged<Tenant>>(`${this.base}/tenants`, { params: params(q) }); }
  createTenant(body: { name: string; code: string; contactEmail?: string }) { return this.http.post<Tenant>(`${this.base}/tenants`, body); }
  updateTenant(id: string, body: { name: string; contactEmail?: string }) { return this.http.put<Tenant>(`${this.base}/tenants/${id}`, body); }
  suspendTenant(id: string, reason: string) { return this.http.post<Tenant>(`${this.base}/tenants/${id}/suspend`, { reason }); }
  resumeTenant(id: string) { return this.http.post<Tenant>(`${this.base}/tenants/${id}/resume`, {}); }

  // Customers
  customers(q?: Query) { return this.http.get<Paged<Customer>>(`${this.base}/customers`, { params: params(q) }); }
  customer(id: string) { return this.http.get<CustomerDetails>(`${this.base}/customers/${id}`); }
  createCustomer(body: Partial<Customer> & { tenantId?: string }) { return this.http.post<CustomerDetails>(`${this.base}/customers`, body); }
  updateCustomer(id: string, body: Partial<Customer>) { return this.http.put<CustomerDetails>(`${this.base}/customers/${id}`, body); }
  setCustomerStatus(id: string, status: CustomerStatus) { return this.http.put<CustomerDetails>(`${this.base}/customers/${id}/status`, { status }); }
  addContact(id: string, body: { name: string; email?: string; phone?: string; jobTitle?: string; isPrimary: boolean }) {
    return this.http.post<CustomerDetails>(`${this.base}/customers/${id}/contacts`, body);
  }
  removeContact(id: string, contactId: string) { return this.http.delete<CustomerDetails>(`${this.base}/customers/${id}/contacts/${contactId}`); }

  // Catalog
  products(q?: Query) { return this.http.get<Paged<Product>>(`${this.base}/products`, { params: params(q) }); }
  createProduct(body: { code: string; name: string; description?: string; tenantId?: string; platforms?: string[] }) { return this.http.post<Product>(`${this.base}/products`, body); }
  updateProduct(id: string, body: { code: string; name: string; description?: string; isActive: boolean; platforms?: string[] }) {
    return this.http.put<Product>(`${this.base}/products/${id}`, body);
  }
  plans(q?: Query) { return this.http.get<Paged<Plan>>(`${this.base}/plans`, { params: params(q) }); }
  plan(id: string) { return this.http.get<Plan>(`${this.base}/plans/${id}`); }
  createPlan(body: SavePlan) { return this.http.post<Plan>(`${this.base}/plans`, body); }
  updatePlan(id: string, body: SavePlan) { return this.http.put<Plan>(`${this.base}/plans/${id}`, body); }
  publishPlan(id: string) { return this.http.post<Plan>(`${this.base}/plans/${id}/publish`, {}); }
  archivePlan(id: string) { return this.http.post<Plan>(`${this.base}/plans/${id}/archive`, {}); }
  newPlanVersion(id: string) { return this.http.post<Plan>(`${this.base}/plans/${id}/new-version`, {}); }

  // Subscriptions
  subscriptions(q?: Query) { return this.http.get<Paged<Subscription>>(`${this.base}/subscriptions`, { params: params(q) }); }
  subscription(id: string) { return this.http.get<SubscriptionDetails>(`${this.base}/subscriptions/${id}`); }
  startSubscription(body: { customerId: string; planId: string; startDate?: string; notes?: string }) {
    return this.http.post<SubscriptionDetails>(`${this.base}/subscriptions`, body);
  }
  subscriptionAction(id: string, action: 'renew' | 'suspend' | 'resume' | 'cancel' | 'change-plan', body: object) {
    return this.http.post<SubscriptionDetails>(`${this.base}/subscriptions/${id}/${action}`, body);
  }

  // Licenses
  licenses(q?: Query) { return this.http.get<Paged<License>>(`${this.base}/licenses`, { params: params(q) }); }
  license(id: string) { return this.http.get<LicenseDetails>(`${this.base}/licenses/${id}`); }
  issueLicense(subscriptionId: string) { return this.http.post<IssuedLicense>(`${this.base}/licenses`, { subscriptionId }); }
  licenseAction(id: string, action: 'suspend' | 'resume' | 'revoke', reason?: string) {
    return this.http.post<LicenseDetails>(`${this.base}/licenses/${id}/${action}`, { reason });
  }
  resetDevice(id: string, activationId: string) {
    return this.http.post<LicenseDetails>(`${this.base}/licenses/${id}/activations/${activationId}/reset`, {});
  }

  // Integrations
  apiClients() { return this.http.get<ApiClient[]>(`${this.base}/api-clients`); }
  createApiClient(body: { name: string; scopes: string[]; tenantId?: string }) { return this.http.post<ApiClientSecret>(`${this.base}/api-clients`, body); }
  updateApiClient(id: string, scopes: string[]) { return this.http.put<ApiClient>(`${this.base}/api-clients/${id}`, { scopes }); }
  apiClientAction(id: string, action: 'disable' | 'enable' | 'revoke') { return this.http.post<ApiClient>(`${this.base}/api-clients/${id}/${action}`, {}); }
  rotateSecret(id: string) { return this.http.post<ApiClientSecret>(`${this.base}/api-clients/${id}/rotate-secret`, {}); }
  signingKeys() { return this.http.get<SigningKeys>(`${this.base}/signing-keys`); }
  rotateSigningKey() { return this.http.post<{ kid: string }>(`${this.base}/signing-keys/rotate`, {}); }

  // Users
  users(q?: Query) { return this.http.get<Paged<User>>(`${this.base}/users`, { params: params(q) }); }
  createUser(body: { email: string; fullName: string; password: string; role: string; tenantId?: string; customerId?: string }) {
    return this.http.post<User>(`${this.base}/users`, body);
  }
  updateUser(id: string, body: { fullName: string; role?: string }) { return this.http.put<User>(`${this.base}/users/${id}`, body); }
  setUserActive(id: string, active: boolean) { return this.http.post<void>(`${this.base}/users/${id}/${active ? 'activate' : 'deactivate'}`, {}); }
  resetPassword(id: string, newPassword: string) { return this.http.post<void>(`${this.base}/users/${id}/reset-password`, { newPassword }); }

  // Audit & reports
  audit(q?: Query) { return this.http.get<Paged<AuditRecord>>(`${this.base}/audit`, { params: params(q) }); }
  overview(tenantId?: string) {
    return this.http.get<Overview>(`${this.base}/reports/overview`, { headers: tenantId ? { 'X-Tenant-Id': tenantId } : {} });
  }
  trend(granularity: 'monthly' | 'yearly', year: number) { return this.http.get<TrendPoint[]>(`${this.base}/reports/trend`, { params: params({ granularity, year }) }); }
  activations(q?: Query) { return this.http.get<Paged<ActivationRow>>(`${this.base}/activations`, { params: params(q) }); }
  regenerateKey(id: string) { return this.http.post<IssuedLicense>(`${this.base}/licenses/${id}/regenerate-key`, {}); }
  /** Reads another tenant's data as a platform admin (explicit tenant scope). */
  scoped<T>(tenantId: string, path: string, q?: Query) {
    return this.http.get<T>(`${this.base}/${path}`, { params: params(q), headers: { 'X-Tenant-Id': tenantId } });
  }
  dashboard() { return this.http.get<Dashboard>(`${this.base}/reports/dashboard`); }
  expiring(days: number) { return this.http.get<ExpiringLicense[]>(`${this.base}/reports/expiring-licenses`, { params: params({ days }) }); }
  activeDevices() { return this.http.get<ActiveDevice[]>(`${this.base}/reports/active-devices`); }
  failedActivations(days: number) { return this.http.get<FailedActivations>(`${this.base}/reports/failed-activations`, { params: params({ days }) }); }
  usage(days: number) { return this.http.get<Usage[]>(`${this.base}/reports/usage`, { params: params({ days }) }); }
}
