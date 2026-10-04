# التشغيل والـ Runbooks

## الإعدادات المطلوبة في الإنتاج

| الإعداد | الوصف |
|---|---|
| `ConnectionStrings:Default` | SQL Server (يُفضّل حساب SQL بصلاحيات `db_datareader`/`db_datawriter` فقط) |
| `ConnectionStrings:Redis` | اختياري؛ مطلوب عند تشغيل أكثر من نسخة من الـ API |
| `Jwt:SigningKey` | 32 بايت على الأقل، من Secret Store |
| `Security:SecretPepper` | قيمة عشوائية طويلة من Secret Store. **تغييرها يُبطل كل مفاتيح المنتجات والأسرار الحالية** |
| `LicenseSigning:KeyStorePath` و `DataProtection:KeyRingPath` | مسارات محمية ومنسوخة احتياطياً، أو استبدال `ISecretStore` بـ Key Vault |
| `Seed:DemoData` | `false` دائماً في الإنتاج |
| `Seed:AdminEmail` / `Seed:AdminPassword` | لإنشاء أول PlatformAdmin فقط، ثم يُحذفان |

## الـ Migrations كخطوة release مستقلة

```bash
dotnet ef migrations script --idempotent -p src/Licensing.Infrastructure -s src/Licensing.Api -o release/migrate.sql
```

يُراجَع السكريبت ويُطبّق قبل نشر الكود الجديد. في الإنتاج يكون `Seed:ApplyMigrations=false`.

## النسخ الاحتياطي (RPO 15 دقيقة / RTO ساعة — مقترح حتى يحددها الـ Product Owner)

- Full backup يومي + Differential كل 6 ساعات + Log backup كل 15 دقيقة (Recovery model = FULL).
- نسخ مجلدات `signing-keys` و`dp-keys` مع كل full backup (بدونها لا يمكن توقيع الرخص ولا فك تشفير المفاتيح).
- Restore drill شهري: استعادة على سيرفر منفصل، تشغيل `/health/ready`، وتسجيل الزمن الفعلي مقابل RTO.

## Runbook — تعطل قاعدة البيانات
1. `/health/ready` يرجع Unhealthy والتنبيه يصل.
2. البرامج المثبتة عند العملاء تستمر offline حتى `OfflineValidUntil` في التوكن الموقّع، فلا يتوقف العملاء فوراً.
3. استعادة الخدمة أو الـ failover، ثم التأكد أن الـ Outbox يفرغ (`messaging.OutboxMessages` حيث `ProcessedAt IS NULL`).

## Runbook — تعطل Redis
لا إجراء عاجل: الـ cache يرجع للقاعدة تلقائياً ويُسجَّل warning. راقب زمن الاستجابة لـ validate، وأعد Redis عند الإمكان.

## Runbook — تسريب مفتاح توقيع الرخص
1. من الواجهة (التكاملات ← تدوير مفتاح التوقيع) أو `POST /api/v1/signing-keys/rotate`.
2. علّم المفتاح المسرّب `Retired` في `licensing.SigningKeys` حتى لا يُنشر للعملاء.
3. البرامج تأخذ توكن جديد في أول heartbeat؛ التوكنز الموقعة بالمفتاح القديم تُرفض offline بعد تحديث قائمة المفاتيح.
4. سجّل الحادثة وراجع `audit.AuditRecords` لعمليات `signing_key.*`.

## Runbook — تسريب Client Secret أو مفتاح منتج
- Client secret: تدوير السر أو سحب الـ client من شاشة التكاملات؛ التوكنز الحالية تنتهي خلال ساعة.
- مفتاح منتج: سحب الرخصة وإصدار رخصة جديدة للعميل؛ الرفض فوري في أول validate.

## المراقبة والتنبيهات المقترحة
- معدل 5xx > 1% لمدة 5 دقائق، وp95 لـ `/licensing/validate` > 300ms.
- `OutboxMessages` غير المعالجة > 1000 أو أي رسالة `DeadLettered = 1`.
- محاولات تفعيل فاشلة لـ tenant واحد > 100 في الساعة (إساءة استخدام محتملة).
- استخدام CPU/IO للـ SQL Server > 80%.

## ما لا يُسجَّل أبداً
مفتاح المنتج الكامل، Client secret، كلمات المرور، الـ access/refresh tokens، وقيم الـ query string. سجل الطلبات لا يحتوي إلا الـ method والمسار والحالة والزمن، والـ Audit يحتفظ ببادئة المفتاح فقط. يوجد اختبار آلي يتحقق أن المفتاح لا يظهر في الرخص ولا الـ Audit ولا الـ Outbox.

## اختبار الحمل
```bash
k6 run -e BASE=http://localhost:5200 tests/load/validate.js
```
