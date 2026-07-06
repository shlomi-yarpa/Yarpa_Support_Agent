# CLAUDE.md – Yarpa Support Agent

מסמך הנחיות לסוכן הקוד (וגם למפתחים) כדי לשמור על מיקוד ועקביות לאורך הפיתוח.
קרא אותו לפני כל משימת פיתוח. אם משהו סותר את המסמכים ב-`docs/`, המסמכים ב-`docs/` הם מקור האמת.

## מה בונים (תמצית)

Agent ל-Windows שאוסף מידע טכני ממחשב לקוח ושולח אותו כ-JSON ל-REST API.
השרת שומר Snapshot, משווה לקודם, מזהה שינויים ומפיק התראות. הכול מוצג ב-CRM.

חוק ברזל: **ה-Agent לא נוגע ב-SQL ולא יודע דבר על מבנה ה-DB.** כל הלוגיקה בשרת.

## Stack

- Agent: C# / .NET 8, Console app, `System.Management` (WMI/CIM), `Microsoft.Win32.Registry`, `System.Text.Json`, Serilog, `Microsoft.Extensions.Hosting` + `HttpClientFactory` + Polly (retry).
- API: ASP.NET Core (.NET 8) Minimal/Controllers, EF Core, SQL Server, Serilog, FluentValidation.
- Contracts: פרויקט `Yarpa.Contracts` עם ה-DTOs המשותפים ל-Agent ול-API (מקור אמת יחיד לסכמת ה-JSON).
- Tests: xUnit.

## מבנה Solution יעד

```
src/
  Yarpa.Contracts/          # DTOs של DiagnosticsSnapshot וכל ה-sections
  Yarpa.Agent.Collectors/   # ICollector + מימוש לכל collector
  Yarpa.Agent/              # Console: config, orchestrator, sender, offline queue
  Yarpa.Api/                # Controllers/Endpoints, DI, auth
  Yarpa.Api.Data/           # DbContext, entities, repositories, migrations
tests/
  Yarpa.Agent.Tests/
  Yarpa.Api.Tests/
```

## עקרונות מנחים

- **מודולריות Collectors:** כל Collector מממש `ICollector` ומחזיר section אחד של המודל.
  הוספה/הסרה של Collector לא משנה שום רכיב אחר. רישום דרך DI. ראה `docs/collectors.md`.
- **בידוד כשלים:** כשל ב-Collector בודד לא מפיל את האיסוף. כל section מקבל סטטוס
  (`ok` / `partial` / `error`) והשגיאה נרשמת בתוך המודל, לא נזרקת החוצה.
- **עמידות שליחה:** retry עם backoff; אם השליחה נכשלת, שמור את ה-JSON בתור מקומי
  ונסה שוב בהרצה הבאה. אין לאבד Snapshot.
- **Idempotency:** כל Snapshot נושא `snapshotId` (GUID) שנוצר ב-Agent. השרת מתעלם
  משליחה כפולה של אותו `snapshotId`.
- **Append-only בשרת:** לעולם לא לדרוס Snapshot קיים. השוואה ושינויים נגזרים, לא הורסים.

## זיהוי לקוח ומחשב (Client identification)

- **API Key לכל לקוח** ב-header `X-Api-Key`, מוגדר ב-`appsettings.json` של ה-Agent. מזהה איזו חברת לקוח שולחת.
- **MachineId** יציב שנגזר ב-Agent: עדיפות ל-`HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`,
  fallback ל-BIOS Serial + MAC ראשי (hash יציב). מזהה איזה מחשב ספציפי.
- השרת מבצע auto-register למחשב חדש בפעם הראשונה תחת הלקוח המאומת.

## קונבנציות קוד

- קבצים מלאים בלבד. אין placeholders, אין `// TODO` שמשאיר פונקציונליות חסרה.
- מונחים טכניים, שמות משתנים, לוגים והערות קוד באנגלית.
- **כל טקסט UI בעברית עם פריסת RTL** (רלוונטי ל-CRM Dashboard בעתיד; אין UI ב-Agent).
- שמות: PascalCase לטיפוסים/מתודות, camelCase למשתנים מקומיים, `I`-prefix לממשקים.
- `System.Text.Json` עם `JsonPropertyName` מפורש בכל DTO כדי לנעול את חוזה ה-JSON.
- כל I/O הוא async. אין `.Result` / `.Wait()`.
- לוגים דרך Serilog בלבד; אין `Console.WriteLine` בקוד ייצור.

## אבטחה

- אין לשמור API keys, connection strings או סודות ב-repo. שימוש ב-`appsettings.json`
  מקומי (מחוץ ל-git) / User Secrets / משתני סביבה.
- HTTPS בלבד בין Agent ל-API.
- ה-API מאמת `X-Api-Key` בכל בקשה לפני עיבוד.
- אין לאסוף מידע אישי מזהה מעבר לנדרש (שם משתמש/מחשב בלבד).

## פקודות (לאחר שה-Solution ייווצר)

```bash
# Build
dotnet build

# הרצת ה-Agent (מצב ידני)
dotnet run --project src/Yarpa.Agent

# הרצת ה-API
dotnet run --project src/Yarpa.Api

# בדיקות
dotnet test

# EF Core migration
dotnet ef migrations add <Name> --project src/Yarpa.Api.Data --startup-project src/Yarpa.Api
dotnet ef database update --project src/Yarpa.Api.Data --startup-project src/Yarpa.Api
```

CLI של ה-Agent (מתוכנן):

```bash
Yarpa.Agent.exe --once          # איסוף ושליחה חד-פעמיים (ברירת מחדל)
Yarpa.Agent.exe --output <path> # שמירת ה-JSON לקובץ בלבד, ללא שליחה (debug)
Yarpa.Agent.exe --dry-run       # איסוף + הדפסת JSON, ללא שליחה
```

## Definition of Done למשימה

- קומפילציה נקייה, אין linter errors.
- Collector חדש: מממש `ICollector`, רשום ב-DI, מטפל בכשלים, יש בדיקה בסיסית.
- שינוי ב-DTO משותף: מתבצע ב-`Yarpa.Contracts` בלבד ומסונכרן בשני הצדדים.
- עדכון `docs/` אם השתנה חוזה (JSON / API / DB).

## לא לעשות עכשיו (Out of scope)

- אין לבנות Windows Service (מתוכנן לעתיד; ראה roadmap).
- אין לבנות את ה-CRM Dashboard בקוד (מאופיין בלבד בשלב זה).
- אין להוסיף תלויות ענן/מסדי נתונים חיצוניים מעבר ל-SQL Server.
