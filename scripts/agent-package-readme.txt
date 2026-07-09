התקנת Yarpa Support Agent
==========================

מה זה: תוכנה שאוספת מידע טכני מהמחשב ושולחת אותו לשרת התמיכה של ירפא.

הערה: אם קיבלת קובץ יחיד בשם YarpaAgentInstaller.exe במקום ה-ZIP הזה - זו הדרך
הפשוטה יותר: לחיצה כפולה על הקובץ, אישור UAC, וזהו. ההוראות שלמטה הן לשיטת
ה-ZIP + PowerShell (למקרה שאין exe יחיד, או שהוא לא עבד מסיבה כלשהי).

מה צריך לפני התחלה:
- קובץ ZIP שקיבלת מירפא (YarpaAgent-Setup.zip).
- הרשאת מנהל (Administrator) על המחשב.
- (מומלץ) מזהה לקוח מה-CRM — קוד הלקוח בבית המרקחת.

שלבי התקנה מומלצים:
1. חלץ את כל תוכן קובץ ה-ZIP לתיקייה זמנית (למשל C:\Temp\YarpaAgent).
2. לחץ קליק ימני על "Windows PowerShell" ובחר "הפעל כמנהל" (Run as administrator).
3. נווט לתיקייה שחילצת אליה, לדוגמה:
       cd "C:\Temp\YarpaAgent"
4. הרץ את פקודת ההתקנה. אם יש לך מזהה לקוח — הוסף אותו:
       .\install-agent.ps1 -SiteCustomerCode <קוד הלקוח>
   לדוגמה:
       .\install-agent.ps1 -SiteCustomerCode 12345
   אם אין מזהה לקוח — אפשר גם בלי:
       .\install-agent.ps1

מה קורה במהלך ההתקנה:
- הקבצים מועתקים אל C:\Program Files\Yarpa\Agent
- מתבצע איסוף ראשוני מיידי והנתונים נשלחים לשרת
- מותקן שירות רקע שדוגם את המחשב אוטומטית אחת לשבוע (בשעות הלילה)

איך יודעים שהצליח:
בסיום מופיעה שורה ירוקה: "Installation complete. Service ... is Running".

אפשרויות הרצה נוספות:
- איסוף חד-פעמי יזום (כשיש תקלה ורוצים מידע עכשיו):
       & "C:\Program Files\Yarpa\Agent\Yarpa.Agent.exe" --once
- התקנה בלי שירות רקע (רק איסוף ראשוני):
       .\install-agent.ps1 -SiteCustomerCode 12345 -NoService

תקלות נפוצות:
- "must be run as Administrator" — לא נפתח PowerShell כמנהל. חזור לשלב 2.
- "API not reachable" — אין כרגע חיבור לשרת. אין צורך לדאוג: התוכנה שומרת מקומית
  ותשלח שוב אוטומטית כשהחיבור יחזור. כדאי לוודא שהמחשב ברשת.

הסרת התוכנה:
       Stop-Service YarpaSupportAgent
       sc.exe delete YarpaSupportAgent
