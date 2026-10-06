# Architecture Decision Records

كل قرار مكتوب بصيغة مختصرة: السياق، القرار، والنتائج. أي انحراف عن هذه القرارات يحتاج ADR جديد قبل الدمج.

## ADR-001 — Modular Monolith على SQL Server
**القرار:** تطبيق واحد (Modular Monolith) بطبقات SharedKernel / Domain / Application / Infrastructure / Api، وكل موديول له namespace وschema خاص في قاعدة البيانات (`tenants`, `identity`, `customers`, `catalog`, `subscriptions`, `licensing`, `integrations`, `audit`, `messaging`, `reporting`).
**تعديل عن الوثيقة:** قاعدة البيانات **SQL Server** بدلاً من PostgreSQL بناءً على طلب صاحب المشروع (السيرفر الموجود عنده). EF Core يجعل التحويل لاحقاً ممكناً.
**النتائج:** نشر واحد، معاملات محلية بسيطة، والفصل بين الموديولات محمي باختبارات المعمارية.

## ADR-002 — TenantId في كل جدول مملوك + Global Query Filters
**القرار:** كل كيان مملوك لـ tenant يطبق `ITenantOwned`، والكيانات الخاصة بعميل تطبق `ICustomerOwned`. الفلاتر تُبنى آلياً لكل الجداول، و`SaveChanges` يرفض أي كتابة لـ tenant آخر. الـ tenant يُقرأ من الـ token فقط.
**النتائج:** العزل لا يعتمد على تذكّر المطوّر، ومُختبر على كل جدول وكل endpoint.

## ADR-003 — Hashing مفتاح المنتج
**القرار:** المفتاح 30 حرفاً من أبجدية 32 رمزاً (150 bit من CSPRNG) بصيغة `XXXXXX-XXXXXX-XXXXXX-XXXXXX-XXXXXX`. يُخزَّن HMAC-SHA256 بـ pepper من الإعدادات + أول 6 أحرف للعرض فقط، ويظهر كاملاً مرة واحدة عند الإصدار.
**تعديل عن الخطة:** صيغة 5×4 تعطي 100 bit فقط وهي أقل من الـ 128 المطلوبة، فالمجموعات 6 أحرف.

## ADR-004 — خوارزمية توقيع الرخص
**القرار:** ECDSA P-256 (ES256) لتوكن الرخصة الموقّع. المفاتيح منفصلة عن مفتاح JWT للمستخدمين، والمفتاح الخاص في Secret Store (ملفات مشفّرة بـ Data Protection في التطوير، وKey Vault في الإنتاج). كل مفتاح له `kid`، والتدوير ينقل المفتاح الحالي إلى `retiring` فيظل يتحقق من التوكنز القديمة.

## ADR-005 — بدون Mediator خارجي
**القرار:** خدمات تطبيق واضحة لكل موديول (commands/queries كـ methods) بدل MediatR، لتجنب تغيّر الترخيص التجاري للمكتبة وتقليل الطبقات. الـ validation عبر FluentValidation filter على كل request.

## ADR-006 — الـ Scheduler
**القرار:** `BackgroundService` داخلي (`ScheduledJobsService`) يشغّل jobs idempotent بالدفعات: انتهاء الاشتراكات والرخص (كل ساعة)، تجميع الاستخدام، تنظيف الـ refresh tokens والـ idempotency، وAudit retention. لا Hangfire/Quartz في الـ MVP؛ تشغيل نسختين في نفس الوقت آمن لأن كل job idempotent.

## ADR-007 — مكتبة الواجهة
**القرار:** Angular standalone + signals بدون مكتبة UI ثقيلة: CSS خاص بخصائص منطقية (logical properties) يدعم RTL/LTR تلقائياً. **ملاحظة:** الإصدار الحالي Angular 21 لأن Node المثبت (22.19) أقدم من متطلبات Angular 22؛ الترقية بـ `ng update` بعد تحديث Node.

## ADR-008 — Outbox / Inbox
**القرار:** الأحداث تُكتب في `messaging.OutboxMessages` في نفس الـ transaction، وdispatcher يرسلها للـ handlers مع retry وexponential backoff وdead-letter بعد 8 محاولات. `InboxMessages` يمنع تشغيل نفس الـ handler مرتين لنفس الحدث.

## ADR-009 — نموذج ملكية الـ Tenant
**القرار:** الـ Tenant هو الشركة البائعة للبرنامج (مالكة المنتجات والخطط)، والـ Customer هو عميلها النهائي. الأدوار: `PlatformAdmin` (كل شيء، ويقدر يحصر العرض في tenant بـ `X-Tenant-Id`)، `TenantAdmin`، `TenantOperator`، و`CustomerUser` (قراءة فقط لبيانات عميله وتقاريره).

## ADR-010 — الـ Access و Refresh tokens
**القرار:** Access token لمدة 15 دقيقة (HS256)، Refresh token عشوائي يُخزَّن HMAC ويتدوّر مع كل استخدام؛ إعادة استخدام توكن مُدوَّر تلغي العائلة كلها. قفل الحساب 15 دقيقة بعد 5 محاولات فاشلة.

## ADR-011 — مصادقة endpoints التفعيل
**القرار:** activate / validate / heartbeat / deactivate تتطلب client token لـ API client بالـ scope المناسب (`licenses.activate` أو `licenses.validate`). الـ tenant يُؤخذ من التوكن، فلا يمكن تفعيل رخصة tenant آخر حتى بمفتاحها.

## ADR-012 — الـ Cache
**القرار:** `IDistributedCache` (Redis عند ضبط `ConnectionStrings:Redis`، وإلا in-memory) لنتيجة validate لمدة 60 ثانية، ويُلغى فوراً عند أي تغيير في الرخصة أو الاشتراك أو العميل أو الـ tenant. أي فشل في الـ cache يُعامل كـ miss والقاعدة هي المرجع.

## ADR-013 — حالات Subscription وLicense
**القرار:** Subscription: `Trial → Active`، `Renew` (Trial/Active/Expired)، `ChangePlan` (Trial/Active)، `Suspend`/`Resume`، `Cancel` (نهائي)، `Expire` (job). License: `Active ↔ Suspended`، `Revoked` (نهائي)، `Expired` (بانتهاء تاريخها أو بانتهاء/إلغاء الاشتراك، وترجع عند التجديد). فترة السماح offline = `HeartbeatInterval + OfflineGraceDays`.

## ADR-012 — واجهة التكامل مع Monitor Cloud
**السياق:** منصة Monitor Cloud تحتاج قراءة العملاء والاشتراكات والخطط، وتجديد/تحرير مقاعد الأجهزة بعد التفعيل بدون مفتاح المنتج، ومتابعة التغييرات أولًا بأول.
**القرار:** `IntegrationController` على `api/v1/integration` لتوكنات الـ API clients فقط، بنطاقات جديدة `customers.read` و`subscriptions.read` و`catalog.read` (LP-1/LP-3)، وبنفس rate limit الخاص بالـ licensing. رد التفعيل والتحقق يتضمن `licenseId` و`customerId` و`subscriptionId`، والتوكن الموقّع يتضمن claim `cid` (LP-2). الـ change feed يقرأ `messaging.OutboxMessages` للـ tenant بترتيب `(OccurredAt, Id)` مع cursor، ولهذا أُضيف عمود `CustomerId` للـ outbox (LP-4). heartbeat وrelease برقم الرخصة (LP-5).
**النتائج:** الحقول والـ claims القديمة لم تتغير، فالبرامج الحالية لا تتأثر. `updatedSince` في قائمة العملاء يعتمد على تاريخ الإنشاء لأن العميل لا يحمل تاريخ تعديل بعد.
