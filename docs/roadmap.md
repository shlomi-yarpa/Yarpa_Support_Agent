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

## שלב 6 – הקשחה ותפעול ✅ הושלם

- אריזה/הפצה של ה-Agent (self-contained single-file exe עם אייקון; `scripts/publish-agent.ps1`).
- הרצה כ-Windows Service (`--service` + `SnapshotWorker`, scheduling תקופתי; `scripts/install-service.ps1`).
- הרשאות (איסוף מרבי ללא admin, `partial` היכן שנחסם), retention קונפיגורבילי (append-only-safe),
  ניטור ותצפיתיות (Serilog עם correlation ל-snapshotId/machineId, `/health`, `/health/ready`, `/metrics`).
- בדיקות עומס (POST /snapshots מקבילי) ואבטחה (X-Api-Key, HTTPS, 413 payload, 429 rate-limit).

תוצר: מערכת מוכנה לפרודקשן. פירוט תפעולי מלא ב-[operations.md](operations.md).

## שלב 7 – אינטגרציה ל-CRM (עתידי / אופציונלי)

בשלב הנוכחי המערכת עצמאית לחלוטין (DB ייעודי נפרד, Dashboard עצמאי). שלב זה יבוצע רק
אם וכאשר יוחלט לשלב עם הדאטה/CRM הקיים.

- קישור `Customer.ExternalCustomerCode` לרשומת הלקוח ב-CRM.
- הטמעת ה-Dashboard ב-CRM ה-web (iframe + deep-link, או צריכת ה-read API ישירות).
- מנגנון אימות משותף בין ה-CRM למערכת.
- אופציונלי: שילוב/סנכרון עם הדאטה הקיים של ה-CRM.

תוצר: Diagnostics מוצג מתוך כרטיס הלקוח ב-CRM.

## סדר עדיפויות

1. שלבים 0-1 (MVP end-to-end) – קריטי, מוכיח את הארכיטקטורה.
2. שלב 2 (כל המידע) – הערך העיקרי לאיסוף.
3. שלבים 3-4 (שינויים + התראות) – הבידול של המערכת.
4. שלבים 5-6 (Dashboard + תפעול) – חשיפה למשתמש הסופי והכנה לפרודקשן.
