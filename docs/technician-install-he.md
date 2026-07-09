# התקנת Yarpa Support Agent — הוראות לטכנאים

מסמך זה מיועד להעתקה למייל או להפצה פנימית.  
קובץ ההתקנה: `YarpaAgent-Setup.zip` (בנתיב השרת: `\\fileserver\d\y_a\YarpaAgent-Setup.zip`).

---

## מה זה?

כלי שאוסף מידע טכני ממחשב הלקוח ושולח אותו לשרת התמיכה של ירפא.  
לאחר ההתקנה הנתונים מוצגים בדשבורד: **http://fileserver:8081**

---

## מה צריך לפני שמתחילים?

- קובץ `YarpaAgent-Setup.zip`
- הרשאת **מנהל (Administrator)** על המחשב
- (מומלץ) **קוד לקוח** מה-CRM של אותו בית מרקחת

---

## התקנה מומלצת — פעם ראשונה

1. חלץ את כל תוכן ה-ZIP לתיקייה, למשל:
   ```
   C:\Temp\YarpaAgent
   ```

2. פתח **PowerShell כמנהל**  
   (קליק ימני על PowerShell → **Run as administrator**)

3. עבור לתיקייה שחילצת:
   ```powershell
   cd "C:\Temp\YarpaAgent"
   ```

4. הרץ התקנה **עם קוד לקוח** (מומלץ):
   ```powershell
   .\install-agent.ps1 -SiteCustomerCode <קוד הלקוח>
   ```
   לדוגמה:
   ```powershell
   .\install-agent.ps1 -SiteCustomerCode 12345
   ```

   אם אין קוד לקוח — אפשר גם בלי:
   ```powershell
   .\install-agent.ps1
   ```

### איך יודעים שהצליח?

בסיום מופיעה שורה ירוקה:

```
Installation complete. Service YarpaSupportAgent is Running
```

---

## מה קורה אחרי ההתקנה?

- מתבצע **איסוף מיידי** — הנתונים נשלחים לשרת ומופיעים בדשבורד
- מותקן **שירות רקע** שדוגם את המחשב **פעם בשבוע** (בשעות הלילה)
- אם אין חיבור לשרת — הנתונים **נשמרים מקומית** ונשלחים אוטומטית כשהחיבור חוזר

---

## אפשרויות הרצה

| מצב | מתי להשתמש | פקודה |
|-----|------------|-------|
| **התקנה מלאה** | פעם ראשונה / החלפת גרסה | `.\install-agent.ps1 -SiteCustomerCode <קוד>` |
| **איסוף מיידי בתקלה** | כשצריך מידע עכשיו | `& "C:\Program Files\Yarpa\Agent\Yarpa.Agent.exe" --once` |
| **התקנה בלי שירות רקע** | רק איסוף חד-פעמי | `.\install-agent.ps1 -SiteCustomerCode <קוד> -NoService` |

---

## צפייה בתוצאות

פתח בדפדפן: **http://fileserver:8081**

ניתן לחפש לפי:
- שם מחשב
- **מזהה לקוח** שהוזן בהתקנה (`-SiteCustomerCode`)

---

## תקלות נפוצות

| שגיאה / מצב | מה לעשות |
|-------------|----------|
| `must be run as Administrator` | לא נפתח PowerShell כמנהל — חזור לשלב 2 |
| `API not reachable` | אין חיבור לשרת כרגע; הנתונים יישמרו ויישלחו מאוחר יותר. ודא שהמחשב ברשת |
| ההתקנה הצליחה אבל לא רואים בדשבורד | המתן דקה ורענן; אם עדיין לא — פנה לתמיכה |

---

## הסרת התוכנה (אם נדרש)

```powershell
Stop-Service YarpaSupportAgent
sc.exe delete YarpaSupportAgent
```

קבצי ההתקנה נשארים ב-`C:\Program Files\Yarpa\Agent` — ניתן למחוק את התיקייה ידנית.

---

## תבנית מייל (להעתקה)

```
נושא: התקנת Yarpa Support Agent בבית מרקחת

שלום,

מצורף קובץ YarpaAgent-Setup.zip להתקנת כלי אבחון טכני במחשב הלקוח.

שלבי התקנה:
1. חלץ את ה-ZIP לתיקייה (למשל C:\Temp\YarpaAgent)
2. פתח PowerShell כמנהל
3. cd "C:\Temp\YarpaAgent"
4. .\install-agent.ps1 -SiteCustomerCode <קוד הלקוח>

דשבורד תמיכה: http://fileserver:8081

בברכה,
[שמך]
```
