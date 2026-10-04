import { Routes, CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { authGuard, permissionGuard } from './core/http';
import { AuthService } from './core/auth.service';
import { Perm } from './core/api.models';
import { Shell } from './layout/shell';
import { Login } from './features/login';

/** Sends each role to its natural first screen. */
const homeRedirect: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return router.createUrlTree([auth.can(Perm.ReportsRead) ? '/dashboard' : '/settings']);
};

export const routes: Routes = [
  { path: 'login', component: Login },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', canActivate: [homeRedirect], children: [] },
      { path: 'dashboard', canActivate: [permissionGuard(Perm.ReportsRead)], loadComponent: () => import('./features/dashboard').then(m => m.DashboardPage) },
      { path: 'tenants', canActivate: [permissionGuard(Perm.TenantsRead)], loadComponent: () => import('./features/tenants').then(m => m.TenantsPage) },
      { path: 'tenants/:id', canActivate: [permissionGuard(Perm.TenantsRead)], loadComponent: () => import('./features/tenant-details').then(m => m.TenantDetailsPage) },
      { path: 'plans', canActivate: [permissionGuard(Perm.CatalogRead)], loadComponent: () => import('./features/plans').then(m => m.PlansPage) },
      { path: 'activations', canActivate: [permissionGuard(Perm.LicensesRead)], loadComponent: () => import('./features/activations').then(m => m.ActivationsPage) },
      { path: 'notifications', canActivate: [permissionGuard(Perm.ReportsRead)], loadComponent: () => import('./features/notifications').then(m => m.NotificationsPage) },
      { path: 'customers', canActivate: [permissionGuard(Perm.CustomersRead)], loadComponent: () => import('./features/customers').then(m => m.CustomersPage) },
      { path: 'customers/:id', canActivate: [permissionGuard(Perm.CustomersRead)], loadComponent: () => import('./features/customer-details').then(m => m.CustomerDetailsPage) },
      { path: 'products', canActivate: [permissionGuard(Perm.CatalogRead)], loadComponent: () => import('./features/catalog').then(m => m.CatalogPage) },
      { path: 'subscriptions', canActivate: [permissionGuard(Perm.SubscriptionsRead)], loadComponent: () => import('./features/subscriptions').then(m => m.SubscriptionsPage) },
      { path: 'subscriptions/:id', canActivate: [permissionGuard(Perm.SubscriptionsRead)], loadComponent: () => import('./features/subscription-details').then(m => m.SubscriptionDetailsPage) },
      { path: 'licenses', canActivate: [permissionGuard(Perm.LicensesRead)], loadComponent: () => import('./features/licenses').then(m => m.LicensesPage) },
      { path: 'licenses/:id', canActivate: [permissionGuard(Perm.LicensesRead)], loadComponent: () => import('./features/license-details').then(m => m.LicenseDetailsPage) },
      { path: 'reports', canActivate: [permissionGuard(Perm.ReportsRead)], loadComponent: () => import('./features/reports').then(m => m.ReportsPage) },
      { path: 'integrations', canActivate: [permissionGuard(Perm.ApiClientsManage, Perm.SigningKeysManage)], loadComponent: () => import('./features/integrations').then(m => m.IntegrationsPage) },
      { path: 'users', canActivate: [permissionGuard(Perm.UsersManage)], loadComponent: () => import('./features/users').then(m => m.UsersPage) },
      { path: 'audit', canActivate: [permissionGuard(Perm.AuditRead)], loadComponent: () => import('./features/audit').then(m => m.AuditPage) },
      { path: 'settings', loadComponent: () => import('./features/settings').then(m => m.SettingsPage) },
      { path: 'forbidden', loadComponent: () => import('./features/misc').then(m => m.ForbiddenPage) },
      { path: '**', loadComponent: () => import('./features/misc').then(m => m.NotFoundPage) },
    ],
  },
];
