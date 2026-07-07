# תפעול, הפצה והקשחה – Yarpa Support Agent (שלב 6)

מסמך זה מרכז את כל הנדרש להבאת המערכת למצב מוכן-לפרודקשן: אריזה והפצה של ה-Agent,
הרצה כ-Windows Service, הרשאות, מדיניות retention בשרת, ניטור ותצפיתיות, ואבטחה.
כל הפקודות והמונחים באנגלית; מקורות האמת לחוזי JSON/API/DB הם המסמכים ב-`docs/`.

---

## 1. אריזה והפצה של ה-Agent

ה-Agent נארז כ-**self-contained single-file** ל-`win-x64`, כך שהוא רץ במחשב הלקוח
**ללא התקנת .NET runtime**. האייקון (`assets/yarpa-agent-icon.ico`) מוטבע ב-EXE.

### בנייה

```powershell
# מפיק dist/agent/win-x64/Yarpa.Agent.exe (+ appsettings.json + config/)
./scripts/publish-agent.ps1

# יעד מותאם
./scripts/publish-agent.ps1 -OutputDir C:\Temp\YarpaAgent
```

הסקריפט מריץ בפועל:

```powershell
dotnet publish src/Yarpa.Agent/Yarpa.Agent.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=none -p:DebugSymbols=false `
    -o dist/agent/win-x64
```

לחלופין דרך publish profile:

```powershell
dotnet publish src/Yarpa.Agent -c Release -p:PublishProfile=win-x64
```

תוצר: קובץ אחד `Yarpa.Agent.exe` (~39MB) לצד `appsettings.json` ותיקיית `config/`.
לפני הפצה יש לערוך ב-`appsettings.json` את `Agent.ApiBaseUrl` ו-`Agent.ApiKey`.

> installer: לא נדרש installer מלא בשלב זה. ההפצה מינימלית — העתקת התיקייה + סקריפט
> ההתקנה של השירות. את הקבצים אפשר לפרוס לתיקייה כגון `C:\Program Files\Yarpa\Agent`.

---

## 2. מצבי הרצה של ה-Agent

| מצב | פקודה | תיאור |
|-----|-------|-------|
| Once (ברירת מחדל) | `Yarpa.Agent.exe --once` | איסוף ושליחה חד-פעמיים ואז יציאה. |
| Dry-run | `Yarpa.Agent.exe --dry-run` | איסוף והדפסת ה-JSON ללוג, ללא שליחה. |
| Output | `Yarpa.Agent.exe --output <path>` | שמירת ה-JSON לקובץ בלבד, ללא שליחה. |
| Service | `Yarpa.Agent.exe --service` | ריצה כתהליך חי שאוסף ושולח בתדירות קונפיגורבילית. |

כל המצבים מריצים את אותו pipeline (Collect → Build model → Send). מצב Service
ומצבי ה-CLI חיים זה לצד זה ואינם משפיעים זה על זה.

### מצב Service (BackgroundService)

בהרצה עם `--service` ה-Agent עולה כ-Generic Host עם `SnapshotWorker` (BackgroundService):
1. בהפעלה מנקז את ה-Offline Queue ואז אוסף ושולח snapshot (אם `RunImmediatelyOnStart=true`).
2. ממתין `IntervalHours` וחוזר על המחזור.
3. כל מחזור מבודד ב-try/catch — כשל אינו מפיל את השירות; ינוסה שוב במחזור הבא.

קונפיגורציה ב-`appsettings.json`:

```json
"Agent": {
  "Service": {
    "IntervalHours": 6,
    "RunImmediatelyOnStart": true
  }
}
```

הערה: נתיבים יחסיים (לוגים, Offline Queue) מעוגנים לתיקיית ההתקנה
(`AppContext.BaseDirectory`), כך שגם כשירות (שה-CWD שלו הוא `System32`) הלוגים
נכתבים לצד ה-EXE. תור ה-Offline ברירת מחדל ב-`%LocalAppData%\Yarpa\OfflineQueue`.

### התקנה/הסרה של השירות

יש להריץ **כ-Administrator**.

```powershell
# התקנה (רושם sc.exe עם "<exe>" --service, start=auto, ומפעיל)
./scripts/install-service.ps1

# נתיב EXE מפורש
./scripts/install-service.ps1 -ExePath "C:\Program Files\Yarpa\Agent\Yarpa.Agent.exe"

# הסרה (עוצר ומוחק)
./scripts/uninstall-service.ps1
```

ידני עם `sc.exe`:

```powershell
sc.exe create YarpaSupportAgent binPath= "\"C:\Program Files\Yarpa\Agent\Yarpa.Agent.exe\" --service" start= auto DisplayName= "Yarpa Support Agent"
sc.exe start   YarpaSupportAgent
sc.exe query   YarpaSupportAgent
sc.exe stop    YarpaSupportAgent
sc.exe delete  YarpaSupportAgent
```

---

## 3. הרשאות (Permissions)

עיקרון: **איסוף מרבי גם ללא admin.** ה-Agent אינו דורש הרשאות מוגברות לריצה רגילה.

- **בידוד כשלים:** כל Collector תופס חריגות; כשל בודד מסומן ב-section עם
  `status = error`/`partial` ואינו מפיל את האיסוף (ראה `CollectionOrchestrator`).
- **סימון `partial`:** היכן שחלק מהמידע חסום (למשל access-denied ל-Event Log), ה-section
  מסומן `partial` עם הסבר בשדה `error`, והאיסוף ממשיך.

טבלת רגישות ל-admin (על בסיס Windows 10/11 טיפוסי):

| Section | דורש admin? | הערות |
|---------|-------------|-------|
| system, os, hardware, disks, network | לא | WMI/CIM ו-APIs זמינים למשתמש רגיל. |
| printers, usbDevices, comPorts, paymentTerminals | לא | WMI + Registry קריאים למשתמש רגיל. |
| services, sqlServer | לא | רשימת שירותים וה-Registry של SQL קריאים ללא admin. |
| installedSoftware | לא | מפתחות Uninstall ב-HKLM/HKCU קריאים. |
| eventLogs | לא (System/Application) | Security log היה דורש admin — לכן אינו נאסף. גישה חסומה → `partial`. |
| yarpaVersion | לא | קריאת `psoftw\piryons.ini` ו-`FileVersionInfo`. |

מסקנה: אין section שחובה עליו admin בקונפיגורציה ברירת המחדל. הרצה כשירות תחת
`LocalSystem` (ברירת מחדל של `sc.exe`) מעניקה גישה מלאה ואינה נדרשת אך אינה מזיקה.

---

## 4. Retention בשרת

מדיניות שמירת snapshots קונפיגורבילית, כ-background job מבוקר. השרת נשאר **append-only**
בזרימה הרגילה; ה-retention רק גוזם snapshots גולמיים ישנים, ולעולם **אינו מוחק Changes
או Alerts היסטוריים**.

חוקי בטיחות (נאכפים תמיד, ללא תלות בהגדרות):
- ה-snapshot האחרון של כל מחשב (`Machines.LastSnapshotId`) לעולם לא נמחק.
- snapshot שאליו מפנה `Change` או `Alert` (SourceSnapshotId) לעולם לא נמחק (שמירת שלמות
  רפרנציאלית והיסטוריה).
- נשמרים לפחות N ה-snapshots האחרונים לכל מחשב, גם אם הם ישנים מהסף.

קונפיגורציה ב-`appsettings.json` תחת `Retention`:

```json
"Retention": {
  "Enabled": false,
  "RetainDays": 180,
  "MinSnapshotsPerMachine": 10,
  "MaxDeletePerRun": 500,
  "ScanIntervalHours": 24
}
```

- `Enabled` — כבוי כברירת מחדל; שום דבר לא נגזם עד להפעלה מפורשת.
- `RetainDays` — snapshots ישנים מסף זה הם מועמדים למחיקה.
- `MinSnapshotsPerMachine` — כמה snapshots אחרונים לשמור לכל מחשב בכל מקרה.
- `MaxDeletePerRun` — תקרת מחיקה בכל ריצה (batch מבוקר).
- `ScanIntervalHours` — תדירות ה-background job.

הפעלה תקופתית: `RetentionHostedService` רץ אוטומטית (כשמאופשר). ניתן להריץ ידנית:

```
POST /api/v1/internal/retention/run     (דורש X-Api-Key)
→ { "enabled", "cutoffUtc", "olderThanCutoff", "protected", "deleted" }
```

---

## 5. ניטור ותצפיתיות (Observability)

### לוגים מובנים (Serilog)

Serilog בשני הצדדים. כל שליחת/קליטת snapshot מעשירה את הלוגים ב-`SnapshotId`
ו-`MachineId` (דרך `LogContext`), כך שניתן לעקוב אחר חוט שלם מהאיסוף ב-Agent ועד
האחסון בשרת לפי אותם מזהי correlation. בשרת פעיל גם `UseSerilogRequestLogging`.

### Health / Readiness

| Endpoint | אימות | תיאור |
|----------|-------|-------|
| `GET /health` | ללא | Liveness — מחזיר `{ "status": "ok" }`. |
| `GET /health/ready` | ללא | Readiness — בודק חיבור DB; `200` מוכן, `503` לא-מוכן. |
| `GET /metrics` | ללא | מדדים תפעוליים בסיסיים (JSON). |

שלושת ה-endpoints התפעוליים אינם דורשים API Key בכוונה (אין בהם מידע לקוח) — כדי
לאפשר בדיקות ע"י load balancers / probes. כל שאר ה-endpoints דורשים `X-Api-Key`.

### מדדים בסיסיים

`GET /metrics` מחזיר מונים מצטברים מאז עליית התהליך:

```json
{
  "startedAtUtc": "...",
  "uptimeSeconds": 1234,
  "snapshotsReceived": 42,
  "snapshotsAccepted": 40,
  "snapshotsDuplicate": 1,
  "snapshotsRejected": 1,
  "snapshotsFailed": 0
}
```

---

## 6. אבטחה (Security)

| נושא | מימוש |
|------|-------|
| אימות `X-Api-Key` | `ApiKeyMiddleware` אוכף על כל בקשה פרט ל-`/health*` ו-`/metrics`. 401 בהיעדר/מפתח לא תקין. |
| HTTPS בלבד | `UseHttpsRedirection` + `UseHsts` (מחוץ ל-Development). ה-Agent מוגדר עם `ApiBaseUrl` ב-HTTPS. |
| אין סודות ב-repo | `appsettings.json` מכיל placeholders בלבד; מפתחות/connection strings ב-User Secrets / env / קבצים מקומיים שאינם ב-git (ראה `.gitignore`). |
| הגבלת גודל payload (413) | `PayloadSizeMiddleware` + מגבלת Kestrel. חריגה מ-`Security.MaxRequestBodyBytes` (ברירת מחדל 5MB) → `413`. |
| Rate limiting (429) | Fixed-window per-API-key (`Security.RateLimit`). חריגה → `429`. חל על endpoints של ה-API (לא על probes). |
| מיפוי סטטוסים | `202` נקלט, `200` כפילות (idempotent), `400` ולידציה, `401` אימות, `413` payload, `429` rate limit. |

קונפיגורציה ב-`appsettings.json` תחת `Security`:

```json
"Security": {
  "MaxRequestBodyBytes": 5242880,
  "RateLimit": { "PermitPerWindow": 10000, "WindowSeconds": 60, "QueueLimit": 0 }
}
```

---

## 7. בדיקות עומס (Load Testing)

### בדיקה אוטומטית (חלק מ-`dotnet test`)

`tests/Yarpa.Api.Tests/LoadTests.cs` שולח 100 בקשות POST במקביל (concurrency 10) עם
payload בגודל `snapshot.json` (~140KB), כל אחת עם `snapshotId` ו-`machineId` ייחודיים,
ומוודא שכולן נקלטות (202) ללא כשלי 5xx. הבדיקה רצה מול DB in-memory (מבודד), לכן
הן משמשות להוכחת נכונות תחת עומס מקבילי — לא כמדד ביצועים של SQL Server.

תוצאה מדודה (סביבת פיתוח, in-memory):

```
Load test: 100 requests, payload ~141.6 KB each
  Concurrency : 10
  Elapsed     : 0.50 s
  Throughput  : ~199 req/s
  Accepted    : 100/100
  5xx failures: 0
```

### בדיקת עומס אמיתית מול API רץ

```powershell
# ברירת מחדל: 200 בקשות, concurrency 20, מול snapshot.json
./scripts/loadtest-snapshots.ps1 -BaseUrl https://localhost:7177 -ApiKey <key>
./scripts/loadtest-snapshots.ps1 -BaseUrl https://host -ApiKey <key> -Total 500 -Concurrency 50
```

הסקריפט מדפיס throughput, התפלגות סטטוסים ואחוזוני זמן תגובה.

---

## 8. פקודות מהירות (סיכום)

```powershell
# Build + test
dotnet build
dotnet test

# אריזת ה-Agent
./scripts/publish-agent.ps1

# שירות (כ-Administrator)
./scripts/install-service.ps1
./scripts/uninstall-service.ps1

# בדיקת עומס מול API רץ
./scripts/loadtest-snapshots.ps1 -BaseUrl https://localhost:7177 -ApiKey <key>
```
