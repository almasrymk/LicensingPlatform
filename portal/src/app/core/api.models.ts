// Typed contract of the Licensing API (/openapi/v1.json). Kept in one file so a generator can replace it later.

export interface Paged<T> { items: T[]; total: number; page: number; pageSize: number; }

export interface ProblemDetails {
  type?: string; title?: string; status?: number; code?: string; traceId?: string;
  errors?: Record<string, string[]>;
}

export interface UserProfile {
  id: string; email: string; fullName: string; role: Role; tenantId?: string; tenantName?: string;
  customerId?: string; customerName?: string; permissions: string[]; language: 'ar' | 'en'; imageUrl?: string;
}
export interface AuthResponse { accessToken: string; expiresAt: string; refreshToken: string; user: UserProfile; }

export type Role = 'PlatformAdmin' | 'TenantAdmin' | 'TenantOperator' | 'CustomerUser';
export const Roles: Role[] = ['PlatformAdmin', 'TenantAdmin', 'TenantOperator', 'CustomerUser'];

export const Perm = {
  TenantsRead: 'tenants.read', TenantsManage: 'tenants.manage',
  CustomersRead: 'customers.read', CustomersManage: 'customers.manage',
  CatalogRead: 'catalog.read', CatalogManage: 'catalog.manage',
  SubscriptionsRead: 'subscriptions.read', SubscriptionsManage: 'subscriptions.manage',
  LicensesRead: 'licenses.read', LicensesManage: 'licenses.manage',
  ApiClientsManage: 'apiclients.manage', UsersManage: 'users.manage',
  AuditRead: 'audit.read', ReportsRead: 'reports.read', SigningKeysManage: 'signingkeys.manage',
} as const;

export type TenantStatus = 'Active' | 'Suspended';
export interface Tenant {
  id: string; name: string; code: string; contactEmail?: string; status: TenantStatus; createdAt: string;
  suspensionReason?: string; customers: number; users: number; activeLicenses: number; products: number; licenses: number; imageUrl?: string;
}

export type CustomerStatus = 'Active' | 'Inactive';
export interface Customer {
  id: string; tenantId: string; tenantName?: string; name: string; email?: string; phone?: string; country?: string;
  taxNumber?: string; status: CustomerStatus; createdAt: string; activeSubscriptions: number; activeLicenses: number; imageUrl?: string;
}
export interface Contact { id: string; name: string; email?: string; phone?: string; jobTitle?: string; isPrimary: boolean; }
export interface CustomerDetails { customer: Customer; contacts: Contact[]; }

export interface Product {
  id: string; tenantId: string; code: string; name: string; description?: string; isActive: boolean; createdAt: string; publishedPlans: number;
  plans: number; licenses: number; platforms: string[]; icon?: string | null; imageUrl?: string;
}

export type PlanStatus = 'Draft' | 'Published' | 'Archived';
export interface Plan {
  id: string; tenantId: string; productId: string; productCode: string; productName: string; code: string; name: string;
  version: number; status: PlanStatus; price: number; currency: string; durationDays?: number | null; trialDays?: number | null;
  maxActivations?: number | null; heartbeatIntervalHours: number; offlineGraceDays: number; features: string[];
  createdAt: string; publishedAt?: string;
}
export interface SavePlan {
  productId: string; code: string; name: string; price: number; currency: string; durationDays: number | null;
  trialDays: number | null; maxActivations: number | null; heartbeatIntervalHours: number; offlineGraceDays: number; features: string[];
}

export type SubscriptionStatus = 'Trial' | 'Active' | 'Suspended' | 'Cancelled' | 'Expired';
export type SubscriptionAction = 'Start' | 'Renew' | 'ChangePlan' | 'Suspend' | 'Resume' | 'Cancel' | 'Expire';
export interface Subscription {
  id: string; tenantId: string; customerId: string; customerName: string; productId: string; productName: string;
  planId: string; planName: string; planVersion: number; status: SubscriptionStatus; startDate: string; endDate?: string;
  trialEndsAt?: string; isLifetime: boolean; version: number; allowedActions: SubscriptionAction[]; licenses: number; createdAt: string;
}
export interface SubscriptionHistory { action: SubscriptionAction; fromStatus?: SubscriptionStatus; toStatus: SubscriptionStatus; at: string; details?: string; }
export interface SubscriptionDetails { subscription: Subscription; history: SubscriptionHistory[]; }

export type LicenseStatus = 'Active' | 'Suspended' | 'Revoked' | 'Expired';
export interface License {
  id: string; tenantId: string; customerId: string; customerName: string; subscriptionId: string; productCode: string;
  planCode: string; planVersion: number; licenseNumber: string; productKeyPrefix: string; status: LicenseStatus;
  statusReason?: string; issuedAt: string; expiresAt?: string; maxActivations?: number | null; activeActivations: number;
  features: string[]; heartbeatIntervalHours: number; offlineGraceDays: number;
}
export interface Activation {
  id: string; licenseId: string; deviceId: string; deviceName?: string; appVersion?: string; status: 'Active' | 'Deactivated';
  activatedAt: string; deactivatedAt?: string; lastHeartbeatAt?: string; lastIpAddress?: string; operatingSystem?: string; online: boolean;
}
export interface LicenseDetails { license: License; activations: Activation[]; }
export interface IssuedLicense { license: License; productKey: string; }

export type ApiClientStatus = 'Active' | 'Disabled' | 'Revoked';
export interface ApiClient {
  id: string; tenantId: string; name: string; clientId: string; scopes: string[]; status: ApiClientStatus;
  createdAt: string; secretRotatedAt: string; lastUsedAt?: string;
}
export interface ApiClientSecret { client: ApiClient; clientSecret: string; }
export const ApiScopes = ['licenses.activate', 'licenses.validate', 'licenses.issue', 'licenses.read', 'customers.read', 'subscriptions.read', 'catalog.read'];

export interface User {
  id: string; email: string; fullName: string; role: Role; tenantId?: string; tenantName?: string; customerId?: string;
  customerName?: string; isActive: boolean; lastLoginAt?: string; createdAt: string; imageUrl?: string;
}

export interface AuditRecord {
  id: number; tenantId?: string; actorType: string; actorId?: string; actorName?: string; action: string; entityType: string;
  entityId?: string; success: boolean; details?: string; ipAddress?: string; correlationId?: string; at: string;
}

export interface Dashboard {
  tenants: number; activeTenants: number; customers: number; products: number; publishedPlans: number;
  activeSubscriptions: number; trialSubscriptions: number; expiredSubscriptions: number;
  activeLicenses: number; suspendedLicenses: number; revokedLicenses: number; activeDevices: number;
  expiringIn7Days: number; expiringIn30Days: number; failedActivations7Days: number; successfulActivations7Days: number;
  activationsTrend: { day: string; successful: number; failed: number }[];
}
export interface ExpiringLicense {
  licenseId: string; licenseNumber: string; customerId: string; customerName: string; productCode: string; planCode: string;
  expiresAt: string; daysLeft: number; activeActivations: number;
}
export interface ActiveDevice {
  activationId: string; licenseId: string; licenseNumber: string; customerName: string; deviceId: string; deviceName?: string;
  appVersion?: string; activatedAt: string; lastHeartbeatAt?: string; stale: boolean;
}
export interface FailedActivations {
  items: { at: string; licenseNumber?: string; customerName?: string; productKeyPrefix?: string; deviceId: string; errorCode?: string; ipAddress?: string }[];
  summary: { errorCode: string; count: number }[];
}
export interface Usage { day: string; customerName: string; activeLicenses: number; activeDevices: number; successfulActivations: number; failedActivations: number; }
export interface SigningKeys { keys: { kid: string; alg: string; status: string }[]; pem: { kid: string; status: string; publicKeyPem: string }[]; }

// ---- Dashboard overview / trend, activations, key regeneration ----
export interface Kpi { value: number; changePercent: number | null; }
export interface ProductShare { productCode: string; productName: string; licenses: number; percent: number; }
export interface RecentActivation { licenseId: string; customerName: string; productName: string; deviceId: string; deviceName?: string; ipAddress?: string; at: string; online: boolean; }
export interface Attention { expiredLicenses: number; renewalsDue: number; limitReached: number; suspiciousActivations: number; }
export interface Overview {
  tenants: Kpi; customers: Kpi; activeLicenses: Kpi; subscriptions: Kpi; revenue: Kpi; currency: string; expiringSoon: number;
  totalLicenses: number; licensesByProduct: ProductShare[]; recentActivations: RecentActivation[]; attention: Attention;
}
export interface TrendPoint { label: string; revenue: number; subscriptions: number; }
export interface ActivationRow {
  id: string; licenseId: string; licenseNumber: string; customerId: string; customerName: string; productCode: string; deviceId: string;
  deviceName?: string; operatingSystem?: string; appVersion?: string; lastIpAddress?: string; status: 'Active' | 'Deactivated';
  activatedAt: string; lastHeartbeatAt?: string; online: boolean;
}
export const Platforms = ['Windows', 'Linux', 'macOS', 'Android', 'iOS', 'Web'];

export type ImageOwner = 'tenants' | 'customers' | 'products' | 'users';
/** Built-in product icons offered when no image is uploaded. */
export const ProductIcons = ['cube', 'server', 'shield', 'monitor', 'code', 'chart', 'key', 'layers', 'activity', 'card', 'users', 'building'];
