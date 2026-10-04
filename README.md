# منصة التراخيص والاشتراكات (Licensing Platform)

منصة SaaS متعددة الشركات (multi-tenant) لإدارة المنتجات والخطط والاشتراكات وإصدار وتفعيل رخص البرامج،
مبنية حسب خطة التنفيذ: Modular Monolith بـ DDD وClean Architecture، ‎.NET 10 وEF Core 10 على **SQL Server**،
وواجهة Angular بالعربي (RTL) والإنجليزي.

## التشغيل محلياً

المتطلبات: .NET SDK 10، SQL Server محلي (Windows Authentication)، Node.js 22.

```bash
cd src/Licensing.Api
dotnet run --launch-profile http
```

- أول تشغيل في بيئة Development ينشئ قاعدة `LicensingPlatform` على `.` ويطبّق الـ migrations ويحمّل البيانات التجريبية.
- الـ API على `http://localhost:5200`، وتوثيق OpenAPI على `/openapi/v1.json`، والـ health على `/health/live` و`/health/ready`.
- الواجهة أثناء التطوير: `cd portal && npm install && npm start` ثم افتح `http://localhost:4200` (الـ proxy يوجّه `/api` للـ API).
- أو انسخ ناتج `npx ng build` من `portal/dist/portal/browser` إلى `src/Licensing.Api/wwwroot`، والواجهة تشتغل من نفس الـ API على `http://localhost:5200`.
- الواجهة Angular 21 لأن Node المثبت 22.19؛ للترقية لـ Angular 22 حدّث Node لـ 22.22.3 أو أحدث ثم `npx ng update @angular/core@22 @angular/cli@22`.

## الحسابات التجريبية (Development فقط)

| الحساب | كلمة المرور | الدور | يشوف إيه |
|---|---|---|---|
| admin@licensing.local | Admin@12345 | PlatformAdmin | كل الشركات وكل البيانات |
| admin@nour.test | Demo@12345 | TenantAdmin | كل بيانات شركة النور للبرمجيات |
| ops@nour.test | Demo@12345 | TenantOperator | تشغيل يومي في شركة النور (بدون مستخدمين وAPI clients) |
| admin@ofoq.test | Demo@12345 | TenantAdmin | كل بيانات شركة الأفق |
| user@alamal.test | Demo@12345 | CustomerUser | اشتراكات ورخص وتقارير مؤسسة الأمل فقط |
| user@alshifa.test | Demo@12345 | CustomerUser | بيانات صيدليات الشفاء فقط |
| user@almanar.test | Demo@12345 | CustomerUser | بيانات مدارس المنار فقط |
| admin@stopped.test | Demo@12345 | TenantAdmin | شركة موقوفة: الدخول مرفوض |

API clients تجريبية: `lc_demo_nour` / `lcs_demo_nour_secret_change_me` و `lc_demo_ofoq` / `lcs_demo_ofoq_secret_change_me`.
مفاتيح رخص تجريبية معروفة موجودة في `DemoAccounts` داخل
[DbInitializer.cs](src/Licensing.Infrastructure/Persistence/DbInitializer.cs) (مثلاً `AMLPRO-DEMQAA-KEY234-ACCNTG-PRQ2Q2`).

> البيانات التجريبية والأسرار في `appsettings.Development.json` للتطوير فقط. في الإنتاج: `Seed:DemoData=false`،
> و`Jwt:SigningKey` و`Security:SecretPepper` من secret store، والـ migrations تُطبّق كخطوة release مستقلة.

## العزل والصلاحيات

- كل جدول مملوك لـ tenant عليه Global Query Filter، و`SaveChanges` يرفض أي كتابة لـ tenant تاني.
- الـ tenant يُقرأ من الـ token فقط، وأي `tenantId` في الـ body يتجاهل لغير الـ PlatformAdmin.
- مستخدم العميل (CustomerUser) عليه فلتر إضافي على `CustomerId`، فالتقارير والـ dashboard بتاعته عن بياناته بس.
- الـ PlatformAdmin يشوف كل حاجة، ويقدر يحصر نفسه في tenant واحد بـ header `X-Tenant-Id`.

## الاختبارات

```bash
dotnet test
```

125 اختبار: Unit (state machine، الخطط، المفاتيح)، Architecture (قواعد الاعتماد)، API من الطرف للطرف (عزل، صلاحيات،
تزامن 50 تفعيل على حد 5، Idempotency، الـ cache بعد السحب، انتهاء الاشتراك، أكواد الأخطاء، Offline token والتدوير، Outbox).
لتشغيلها على SQL Server الحقيقي بدل SQLite:

```bash
LICENSING_TEST_SQLSERVER=1 dotnet test
```

اختبارات الواجهة: `cd portal && npx ng test --watch=false`.

## العميل المرجعي (يحاكي البرنامج على جهاز العميل)

```bash
dotnet run --project tools/Licensing.ReferenceAgent -- activate --key AMLPOS-DEMQAA-KEY234-PQSPQS-BASZC2
```

بعدها `check` يتحقق offline من التوكن الموقّع بالمفتاح العام فقط، و`heartbeat` يجدده، و`run --minutes 5` يعمل heartbeat عند `check_after` ويكمل offline لو السيرفر مش متاح، و`deactivate` يحرر الجهاز.

## التوثيق

- [ADRs](docs/adr/README.md): القرارات المعمارية الثلاثة عشر.
- [التشغيل](docs/operations.md): الإعدادات، الـ migrations، النسخ الاحتياطي، الـ Runbooks، والمراقبة.

## الهيكل

```
src/
  Licensing.SharedKernel     Entity, AggregateRoot, ValueObject, Result, DomainEvent
  Licensing.Domain           Tenants, Identity, Customers, Catalog, Subscriptions, Licensing, Integrations, Audit, Reporting
  Licensing.Application      خدمات كل موديول (commands/queries)، الـ validators، event handlers
  Licensing.Infrastructure   EF Core (SQL Server)، العزل، Outbox/Inbox، Idempotency، التشفير والتوقيع، الـ Jobs، البيانات التجريبية
  Licensing.Api              Controllers، المصادقة والصلاحيات، ProblemDetails، Rate limiting، Health
tests/Licensing.Tests        اختبارات Unit وArchitecture وAPI
tests/load                   سكريبت k6 للحمل
tools/Licensing.ReferenceAgent  العميل المرجعي
portal/                      واجهة Angular
docs/                        ADRs والتشغيل
```
