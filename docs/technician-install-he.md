# התקנת Yarpa Support Agent — הוראות לטכנאים

מסמך זה מיועד להעתקה למייל או להפצה פנימית.

קובץ ההתקנה (מומלץ): **`YarpaAgentInstaller.exe`** — קובץ אחד, הפעלה בלחיצה כפולה.

---

## מה זה?

כלי שאוסף מידע טכני ממחשב הלקוח ושולח אותו לשרת התמיכה של ירפא.
לאחר ההתקנה הנתונים מוצגים בדשבורד: **http://fileserver:8081**

---

## מה צריך לפני שמתחילים?

- קובץ `YarpaAgentInstaller.exe`
- הרשאת **מנהל (Administrator)** על המחשב
- (מומלץ) **קוד לקוח** מה-CRM של אותו בית מרקחת

---

## התקנה — שיטה מומלצת (קובץ אחד)

1. העתק/הורד את `YarpaAgentInstaller.exe` למחשב הלקוח.
2. **לחיצה כפולה** על הקובץ.
3. יופיע חלון שחור (קונסולה) שמבקש **Site/customer code** — הקלד את קוד הלקוח מה-CRM
   ולחץ Enter. אם אין קוד — פשוט לחץ Enter בלי להקליד.
4. יופיע חלון אבטחה של Windows (**UAC**) שמבקש הרשאת מנהל — לחץ **Yes / כן**.
5. ייפתח חלון PowerShell שמריץ את ההתקנה בפועל. בסיום מופיעה שורה ירוקה:
   ```
   Installation complete. Service YarpaSupportAgent is Running
   ```
6. סגור את החלונות (אפשר ללחוץ Enter/כל מקש בחלון השחור לסגירה).

זהו — לא צריך PowerShell, לא צריך לחלץ ZIP, לא צריך שום פרמטר בשורת פקודה.

---

## שיטה חלופית — ZIP + PowerShell (למקרה שהריצה בלחיצה כפולה לא עבדה)

1. חלץ את כל תוכן `YarpaAgent-Setup.zip` לתיקייה, למשל `C:\Temp\YarpaAgent`.
2. פתח **PowerShell כמנהל** (קליק ימני על PowerShell → **Run as administrator**).
3. עבור לתיקייה שחילצת:
   ```powershell
   cd "C:\Temp\YarpaAgent"
   ```
4. הרץ התקנה **עם קוד לקוח** (מומלץ):
   ```powershell
   .\install-agent.ps1 -SiteCustomerCode <קוד הלקוח>
   ```
   אם אין קוד לקוח — אפשר גם בלי:
   ```powershell
   .\install-agent.ps1
   ```
   אם Windows חוסם את ההרצה (`UnauthorizedAccess` / "cannot be loaded"), הרץ במקום זאת:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install-agent.ps1 -SiteCustomerCode <קוד הלקוח>
   ```

### איך יודעים שהצליח?

בסיום מופיעה שורה ירוקה:

```
Installation complete. Service YarpaSupportAgent is Running
```

---

## מה קורה אחרי ההתקנה? (בשתי השיטות)

- מתבצע **איסוף מיידי** — הנתונים נשלחים לשרת ומופיעים בדשבורד
- מותקן **שירות רקע** שדוגם את המחשב **פעם בשבוע** (בשעות הלילה)
- אם אין חיבור לשרת — הנתונים **נשמרים מקומית** ונשלחים אוטומטית כשהחיבור חוזר

---

## אפשרויות הרצה נוספות (דורש את ה-ZIP / PowerShell)

| מצב | מתי להשתמש | פקודה |
|-----|------------|-------|
| **איסוף מיידי בתקלה** | כשצריך מידע עכשיו | `& "C:\Program Files\Yarpa\Agent\Yarpa.Agent.exe" --once` |
| **התקנה בלי שירות רקע** | רק איסוף חד-פעמי | `.\install-agent.ps1 -SiteCustomerCode <קוד> -NoService` |

---

## צפייה בתוצאות

פתח בדפדפן: **http://fileserver:8081**

ניתן לחפש לפי:
- שם מחשב
- **מזהה לקוח** שהוזן בהתקנה

---

## תקלות נפוצות

| שגיאה / מצב | מה לעשות |
|-------------|----------|
| Windows חוסם את ה-exe/ps1 ("Windows protected your PC") | לחץ "More info" → "Run anyway", או השתמש בפקודת ה-`ExecutionPolicy Bypass` בשיטה החלופית |
| לא הופיע חלון UAC / לא קרה כלום בלחיצה כפולה | ודא שאתה מריץ מהמשתמש שלך (לא צריך להיות מנהל בעצמך — Windows יבקש הרשאה); נסה קליק ימני → "Run as administrator" על הקובץ |
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

מצורף קובץ YarpaAgentInstaller.exe להתקנת כלי אבחון טכני במחשב הלקוח.

שלבי התקנה:
1. לחיצה כפולה על הקובץ.
2. אם מבקש קוד לקוח - הקלד אותו ולחץ Enter (אפשר גם לדלג עם Enter).
3. לחץ "כן" (Yes) בחלון האבטחה של Windows שנפתח.
4. המתן עד שמופיעה שורה ירוקה "Installation complete".

דשבורד תמיכה: http://fileserver:8081

בברכה,
[שמך]
```
