# מפת דרכים – Yarpa Support Agent

שלבי פיתוח מדורגים. כל שלב מספק ערך עצמאי וניתן לבדיקה. הסדר תוכנן כך שנשאר ממוקדים
ומגיעים ל-end-to-end עובד מוקדם ככל האפשר.

## שלב 0 – תשתית ו-Solution

- יצירת Solution ופרויקטים: `Yarpa.Contracts`, `Yarpa.Agent.Collectors`,
  `Yarpa.Agent`, `Yarpa.Api`, `Yarpa.Api.Data`, פרויקטי טסטים.
- הגדרת Serilog, DI, configuration בשני הצדדים.
- הגדרת ה-DTOs הבסיסיים של `DiagnosticsSnapshot` ב-`Yarpa.Contracts`.

תוצר: Solution שמתקמפל, שלד ריק שרץ.

## שלב 1 – MVP end-to-end (הליבה)

מטרה: Agent שאוסף מעט מידע ושולח, שרת ששומר. חוט שלם מקצה לקצה.

- Agent: `ICollector`, `CollectionOrchestrator`, `MachineIdentity`, `SnapshotSender`.
- Collectors ראשונים: `system`, `os`, `hardware`, `disks`.
- API: `POST /api/v1/snapshots` + ApiKey middleware + auto-register של Machine.
- DB: `Customers`, `ApiKeys`, `Machines`, `Snapshots` (עם `RawJson`).
- שמירת Snapshot append-only + idempotency לפי `snapshotId`.
- Offline Queue בסיסי ב-Agent.

תוצר: הרצת ה-Agent שומרת Snapshot ב-DB דרך ה-API.

## שלב 2 – כל ה-Collectors

- השלמת שאר ה-Collectors: `network`, `printers`, `usbDevices`, `comPorts`,
  `paymentTerminals`, `services`, `sqlServer`, `installedSoftware`, `eventLogs`, `yarpaVersion`.
- טבלת מיפוי VID/PID ליצרני סליקה.
- בידוד כשלים מלא (`partial`/`error` לכל section).
- העמודות המפוענחות ב-`Snapshots` לשאילתות מהירות.

תוצר: איסוף מלא של כל המידע המאופיין.

## שלב 3 – השוואה ושינויים

- `SnapshotComparer` בשרת: השוואה ל-Snapshot הקודם ויצירת רשומות `Change`.
- טבלת `Changes` + endpoint `GET /machines/{id}/changes` (Timeline).
- כל סוגי השינויים לפי [specification.md](specification.md).

תוצר: היסטוריית שינויים מלאה לכל מחשב.

## שלב 4 – Alerts

- `AlertEngine` + טבלת `Alerts`.
- חוקים ראשוניים: ServiceDown, DiskAlmostFull, PaymentTerminalMissing, SqlNotRunning,
  OldSoftwareVersion, NoRecentContact, CollectorError.
- ספים קונפיגורביליים בשרת.
- סגירה אוטומטית של התראות שחלפו.
- endpoint `GET /machines/{id}/alerts`.

תוצר: התראות אוטומטיות לאיש התמיכה.

## שלב 5 – CRM Diagnostics Dashboard

- endpoints לקריאה: `machines`, `summary`, `snapshots`, `snapshot/{id}`.
- מסכי Summary / Timeline / Alerts בתוך ה-CRM (עברית, RTL).

תוצר: איש התמיכה רואה תמונת מצב מלאה תוך שניות.

## שלב 6 – הקשחה ותפעול

- אריזה/הפצה של ה-Agent (self-contained exe / installer).
- הרצה כ-Windows Service (scheduling תקופתי).
- הרשאות, retention, ניטור ותצפיתיות.
- בדיקות עומס ואבטחה.

תוצר: מערכת מוכנה לפרודקשן.

## סדר עדיפויות

1. שלבים 0-1 (MVP end-to-end) – קריטי, מוכיח את הארכיטקטורה.
2. שלב 2 (כל המידע) – הערך העיקרי לאיסוף.
3. שלבים 3-4 (שינויים + התראות) – הבידול של המערכת.
4. שלבים 5-6 (Dashboard + תפעול) – חשיפה למשתמש הסופי והכנה לפרודקשן.
