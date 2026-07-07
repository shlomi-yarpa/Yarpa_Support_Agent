# Yarpa Support Agent

מערכת לאיסוף אוטומטי של מידע טכני ממחשבי לקוחות של Yarpa, שליחתו לשרת מרכזי,
שמירת היסטוריה מלאה, זיהוי שינויים והתראות, והצגתם בתוך ה-CRM של צוות התמיכה.

המטרה: שאיש התמיכה יקבל תוך שניות תמונת מצב מלאה ועדכנית של סביבת הלקוח,
כולל היסטוריית שינויים והתראות, עוד לפני שהוא מתחיל לטפל בתקלה.

## מטרות עסקיות

- לקצר משמעותית את זמן הטיפול בקריאות שירות.
- לזהות מיידית שינויים שבוצעו במחשב הלקוח.
- לשמור היסטוריה מלאה של סביבת העבודה של כל לקוח.
- לצמצם טעויות אנוש באיסוף מידע ידני.
- להוות בסיס למערכת ניטור ותמיכה עתידית.

## רכיבי המערכת

1. **Yarpa Agent** – אפליקציית Console ב-.NET 8 ל-Windows. אוספת מידע דרך רכיבי
   `Collector` מודולריים, בונה מודל JSON אחד (`DiagnosticsSnapshot`), ושולחת אותו
   ב-HTTPS ל-REST API. ה-Agent אינו מתחבר ל-SQL ואינו יודע דבר על מבנה מסד הנתונים.
2. **REST API** – שירות ASP.NET Core (.NET 8). אחראי על אימות, זיהוי הלקוח/מחשב,
   שמירת Snapshot, השוואה ל-Snapshot קודם, יצירת שינויים והתראות.
3. **SQL Server** – אחסון append-only. כל שליחה יוצרת Snapshot חדש; אין דריסה.
4. **CRM Diagnostics Dashboard** – מסכי Summary / Timeline / Alerts בתוך ה-CRM
   הקיים (מאופיין כאן, ייבנה בשלב מאוחר יותר).

## זרימת נתונים

```
Yarpa Agent -> Collectors -> JSON Model -> REST API -> SQL Server -> CRM Dashboard
```

## מבנה התיקיות (מתוכנן)

```
Yarpa_Support_Agent/
  README.md
  CLAUDE.md
  docs/
    architecture.md          # ארכיטקטורה, שכבות, זרימת נתונים
    specification.md         # אפיון פונקציונלי + לא-פונקציונלי
    collectors.md            # פירוט כל Collector
    data-model-and-api.md    # סכמת JSON, חוזה REST API, סכמת DB
    roadmap.md               # שלבי פיתוח
  src/                       # ייווצר בשלב הפיתוח
    Yarpa.Agent/             # Console app (.NET 8)
    Yarpa.Agent.Collectors/  # רכיבי איסוף מודולריים
    Yarpa.Contracts/         # מודלי ה-DTO המשותפים ל-Agent ול-API
    Yarpa.Api/               # ASP.NET Core Web API
    Yarpa.Api.Data/          # EF Core + Repositories
  tests/
    Yarpa.Agent.Tests/
    Yarpa.Api.Tests/
```

## מסמכי תכנון

- [docs/architecture.md](docs/architecture.md) – ארכיטקטורה מלאה ותרשימים.
- [docs/specification.md](docs/specification.md) – אפיון מדויק ודרישות.
- [docs/collectors.md](docs/collectors.md) – פירוט כל רכיב איסוף.
- [docs/data-model-and-api.md](docs/data-model-and-api.md) – מודל נתונים, API ו-DB.
- [docs/operations.md](docs/operations.md) – תפעול, הפצה, שירות, retention, אבטחה, עומס.
- [docs/roadmap.md](docs/roadmap.md) – מפת דרכים ושלבי פיתוח.
- [CLAUDE.md](CLAUDE.md) – הנחיות עבודה לסוכן קוד (stack, קונבנציות, פקודות).

## טכנולוגיה

- Agent: C# / .NET 8 (Console), `System.Management` (WMI/CIM), Serilog.
- API: ASP.NET Core (.NET 8), EF Core, SQL Server.
- העברת נתונים: JSON over HTTPS.
- פלטפורמה: Windows בלבד.

## סטטוס נוכחי

שלבים 0–6 הושלמו: איסוף מלא, שליחה עמידה, אחסון append-only, השוואה ושינויים, התראות,
CRM Dashboard, והקשחה ותפעול (אריזה self-contained, Windows Service, retention, ניטור,
אבטחה, בדיקות עומס). ראה [docs/operations.md](docs/operations.md) להרצה, הפצה ותפעול.
שלב 7 (אינטגרציה ל-CRM) עתידי/אופציונלי.

### הרצה מהירה

```bash
dotnet build
dotnet test
dotnet run --project src/Yarpa.Api          # REST API
dotnet run --project src/Yarpa.Agent -- --once   # איסוף ושליחה חד-פעמיים
./scripts/publish-agent.ps1                  # אריזת ה-Agent ל-single-file exe
```
