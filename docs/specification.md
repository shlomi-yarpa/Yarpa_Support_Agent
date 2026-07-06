# אפיון מפורט – Yarpa Support Agent

מסמך זה מגדיר את הדרישות הפונקציונליות והלא-פונקציונליות, לוגיקת ההשוואה,
חוקי ההתראות ומסכי ה-CRM. מהווה מקור אמת לפיתוח יחד עם [data-model-and-api.md](data-model-and-api.md).

## 1. Actors ותרחישים

- **Yarpa Agent** – רץ במחשב הלקוח, אוסף ושולח.
- **REST API** – מקבל, מזהה, שומר, משווה, מתריע.
- **איש תמיכה** – צורך את המידע דרך ה-CRM Dashboard.

תרחיש ראשי:
1. ה-Agent מופעל (ידני / CLI / מתוך Yarpa).
2. אוסף מידע מכל ה-Collectors ובונה `DiagnosticsSnapshot`.
3. שולח `POST /api/v1/snapshots` עם `X-Api-Key` ו-`MachineId`.
4. השרת מאמת, מזהה/רושם את המחשב, שומר Snapshot, משווה לקודם, מפיק Changes ו-Alerts.
5. איש התמיכה רואה ב-CRM תמונת מצב + היסטוריה + התראות.

## 2. דרישות פונקציונליות – Agent

- **FR-A1**: לאסוף את כל קטגוריות המידע המפורטות ב-[collectors.md](collectors.md).
- **FR-A2**: לבנות מודל JSON אחיד (`DiagnosticsSnapshot`) לפי [data-model-and-api.md](data-model-and-api.md).
- **FR-A3**: לחשב `MachineId` יציב (MachineGuid → fallback BIOS Serial + MAC).
- **FR-A4**: ליצור `snapshotId` (GUID) חדש לכל איסוף.
- **FR-A5**: לשלוח את המודל ב-HTTPS ל-REST API עם `X-Api-Key`.
- **FR-A6**: retry עם backoff בכשל תקשורת; שמירה ב-Offline Queue מקומי אם כל הניסיונות נכשלו.
- **FR-A7**: להתחדש בהרצה הבאה ולשלוח snapshots שממתינים בתור.
- **FR-A8**: בידוד כשלים – כשל ב-Collector בודד מסומן ב-section עם סטטוס `error` ולא מפיל את האיסוף.
- **FR-A9**: תמיכה ב-flags: `--once` (ברירת מחדל), `--output <path>`, `--dry-run`.
- **FR-A10**: לוג מקומי (Serilog) של האיסוף והשליחה.

## 3. דרישות פונקציונליות – Server

- **FR-S1**: לאמת `X-Api-Key` ולזהות את ה-Customer; לדחות 401 אם לא תקין.
- **FR-S2**: לזהות `Machine` לפי `MachineId` תחת ה-Customer; לבצע auto-register אם חדש.
- **FR-S3**: לשמור כל Snapshot כ-append-only (כולל JSON גולמי + עמודות מפוענחות).
- **FR-S4**: idempotency – התעלמות מ-`snapshotId` שכבר נקלט.
- **FR-S5**: להשוות ל-Snapshot הקודם ולייצר רשומות `Change`.
- **FR-S6**: להפעיל את חוקי ה-Alerts ולייצר `Alert`.
- **FR-S7**: לחשוף endpoints לקריאה עבור ה-CRM (Summary, History/Timeline, Changes, Alerts).
- **FR-S8**: לא לדרוס לעולם מידע היסטורי.

## 4. לוגיקת השוואת Snapshots

בעת קליטת Snapshot חדש, השרת משווה אותו ל-Snapshot האחרון של אותו Machine ומייצר
רשומות `Change` עבור כל שינוי משמעותי. סוגי שינויים:

- **DeviceAdded** – התקן USB / מדפסת / מסוף סליקה שנוסף.
- **DeviceRemoved** – התקן שנעלם.
- **ComPortChanged** – שינוי מספר COM Port של התקן.
- **OsChanged** – שינוי גרסת/Build של Windows.
- **SqlChanged** – שינוי בהתקנה/Instance/גרסה/סטטוס של SQL Server.
- **PrinterChanged** – הוספה/הסרה/שינוי מדפסת ברירת מחדל.
- **SoftwareVersionChanged** – שינוי גרסת תוכנת Yarpa או רכיב מנוטר.
- **RamChanged** – שינוי בכמות ה-RAM.
- **DiskChanged** – שינוי בדיסקים / ירידה משמעותית במקום פנוי.
- **ServiceStateChanged** – שירות מנוטר שעבר בין רץ ללא-רץ.
- **NetworkChanged** – שינוי IP/Gateway/DNS משמעותי.

לכל `Change` נשמרים: הסוג, ה-section, ערך קודם, ערך חדש, וחותמת זמן.

## 5. חוקי Alerts

התראות נגזרות מה-Snapshot ומה-Changes. כללים ראשוניים (ניתנים להרחבה בשרת):

- **ServiceDown** – שירות Yarpa / SQL מנוטר אינו רץ.
- **DiskAlmostFull** – מקום פנוי בדיסק מתחת לסף (למשל < 10% או < 5GB).
- **PaymentTerminalMissing** – מסוף סליקה שהיה קיים ונעלם.
- **SqlNotRunning** – SQL Server מותקן אך השירות אינו פעיל.
- **OldSoftwareVersion** – גרסת תוכנת Yarpa ישנה מהמינימום הנתמך.
- **NoRecentContact** – לא התקבל Snapshot מהמחשב מעל X ימים.
- **CollectorError** – section קריטי נכשל באיסוף.

לכל `Alert`: סוג, חומרה (`info` / `warning` / `critical`), הודעה בעברית, מצב (`open` / `resolved`), וחותמת זמן.
התראה נסגרת אוטומטית כאשר Snapshot חדש מראה שהתנאי חלף.

## 6. דרישות לא-פונקציונליות

- **NFR-1 ביצועים**: איסוף מלא עד ~30 שניות במחשב טיפוסי; שליחה עד ~10 שניות.
- **NFR-2 עמידות**: אין crash; כל שגיאה נתפסת ונרשמת; snapshot לא אובד (Offline Queue).
- **NFR-3 הרשאות**: פעולה מרבית ללא admin; היכן שנדרש admin – סימון `partial` והמשך.
- **NFR-4 אבטחה**: HTTPS בלבד; API Key לכל בקשה; ללא סודות ב-repo; מינימום מידע אישי.
- **NFR-5 תאימות**: Windows 10/11 ו-Windows Server נתמכים; .NET 8 runtime (או self-contained).
- **NFR-6 גודל**: ה-Agent קל משקל, ללא UI, זמן הפעלה מהיר.
- **NFR-7 תחזוקתיות**: מודולריות Collectors; הוספה/הסרה ללא שינוי שאר המערכת.
- **NFR-8 תצפיתיות**: לוגים מובנים (Serilog) בשני הצדדים.

## 7. מסכי CRM Diagnostics Dashboard (אפיון, לא לבנייה עכשיו)

כל הטקסט בעברית, פריסת RTL.

### 7.1 Summary
תמונת מצב עדכנית של המחשב הנבחר:
- מערכת הפעלה (גרסה, Build, Edition).
- גרסת תוכנת Yarpa.
- SQL Server (מותקן / Instances / סטטוס / גרסה).
- מסוף סליקה (דגם, יצרן, COM Port, סטטוס).
- מדפסות (רשימה + ברירת מחדל).
- סורקים / קוראי ברקוד.
- מקום פנוי בדיסק, RAM.
- זמן העדכון האחרון (Snapshot אחרון).

### 7.2 Timeline
ציר זמן של כל ה-Changes שזוהו במחשב לאורך זמן, ממוין מהחדש לישן, עם אפשרות
לצפות ב-Snapshot מלא בכל נקודת זמן.

### 7.3 Alerts
רשימת התראות פתוחות וסגורות, ממוינות לפי חומרה וזמן, עם קישור ל-Change/Snapshot הרלוונטי.

## 8. הנחות ופתוחים

- מיפוי VID/PID של יצרני סליקה (Ingenico/Verifone/PAX/Castles) יתוחזק כטבלת התאמה
  בשרת/ב-Agent; ראה [collectors.md](collectors.md).
- זיהוי גרסת Yarpa: מנגנון הזיהוי (Registry / קובץ גרסה / DLL version) ייקבע ב-[collectors.md](collectors.md).
- ספי ה-Alerts (דיסק, גרסת מינימום, ימי אי-תקשורת) יהיו קונפיגורביליים בשרת.
