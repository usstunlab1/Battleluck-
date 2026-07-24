# BattleLuck Documentation (العربية)

## ملخص المشروع

BattleLuck هو ملحق برمجي متطور لخوادم لعبة V Rising، يعمل من جهة الخادم (Server-side) باستخدام BepInEx وIL2CPP.

## المميزات الرئيسية

- **إدارة الأحداث**: أوضاع لعب تنافسية وتعاونية (Bloodbath, Colosseum, Siege)
- **التحكم في الشخصيات**: إدارة الأعداء والزعماء بأوامر مخصصة
- **نظام التراجع**: لقطات تلقائية لحالة اللاعبين مع استعادة آمنة
- **كتالوج الإجراءات**: أكثر من 202 إجراء مسجل
- **الذكاء الاصطناعي**: تكامل اختياري مع Ollama أو مزودات خارجية

## المتطلبات

- V Rising Dedicated Server
- BepInEx + VampireCommandFramework
- .NET 6 SDK

## التثبيت

```powershell
dotnet build -c Release
# انسخ BattleLuck.dll إلى BepInEx/plugins/
```

## وثائق إضافية

- [Administration](docs/ADMIN.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Development](docs/DEVELOPMENT.md)
- [Changelog](CHANGELOG.md)

## الرخصة

GNU Affero General Public License v3.0