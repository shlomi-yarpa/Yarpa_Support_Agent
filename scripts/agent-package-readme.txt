התקנת Yarpa Support Agent
==========================

מה זה: תוכנה שאוספת מידע טכני מהמחשב ושולחת אותו לשרת התמיכה של ירפא.

מה צריך לפני התחלה:
- מפתח לקוח (API key) שקיבלת מירפא. נראה כך: yk-xxxxxxxx...
- הרשאת מנהל (Administrator) על המחשב.

שלבי התקנה:
1. חלץ את כל תוכן קובץ ה-ZIP לתיקייה זמנית (למשל C:\Temp\YarpaAgent).
2. לחץ קליק ימני על "Windows PowerShell" ובחר "הפעל כמנהל" (Run as administrator).
3. נווט לתיקייה שחילצת אליה, לדוגמה:
       cd "C:\Temp\YarpaAgent"
4. הרץ את פקודת ההתקנה עם המפתח שקיבלת:
       .\install-agent.ps1 -ApiKey <המפתח>
   לדוגמה:
       .\install-agent.ps1 -ApiKey yk-1234abcd5678...

מה קורה במהלך ההתקנה:
- הקבצים מועתקים אל C:\Program Files\Yarpa\Agent
- מתבצע איסוף ראשוני מיידי והנתונים נשלחים לשרת
- מותקן שירות רקע שדוגם את המחשב אוטומטית אחת לשבוע (בשעות הלילה)

איך יודעים שהצליח:
בסיום מופיעה שורה ירוקה: "Installation complete. Service ... is Running".

תקלות נפוצות:
- "must be run as Administrator" — לא נפתח PowerShell כמנהל. חזור לשלב 2.
- "API not reachable" — אין כרגע חיבור לשרת. אין צורך לדאוג: התוכנה שומרת מקומית
  ותשלח שוב אוטומטית כשהחיבור יחזור. כדאי לוודא שהמחשב ברשת ושהכתובת נכונה.

איסוף חד-פעמי יזום (כשיש תקלה ורוצים מידע עכשיו):
       & "C:\Program Files\Yarpa\Agent\Yarpa.Agent.exe" --once

הסרת התוכנה:
       Stop-Service YarpaSupportAgent
       sc.exe delete YarpaSupportAgent
