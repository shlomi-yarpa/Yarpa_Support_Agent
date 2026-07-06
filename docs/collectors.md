# Collectors – פירוט רכיבי האיסוף

כל Collector הוא רכיב עצמאי המממש `ICollector` ומחזיר section אחד של המודל.
הוספה/הסרה של Collector נעשית דרך רישום DI בלבד, ללא שינוי בשאר המערכת.

## ממשק משותף

```csharp
public interface ICollector
{
    string SectionName { get; }
    Task<CollectorResult> CollectAsync(CancellationToken ct);
}

public sealed class CollectorResult
{
    public string SectionName { get; init; }
    public CollectorStatus Status { get; init; } // Ok | Partial | Error
    public object? Data { get; init; }
    public string? Error { get; init; }
    public long DurationMs { get; init; }
}
```

עקרונות לכל Collector:
- לבודד כשלים: תפיסת חריגות והחזרת `Status = Error` עם הודעה, לעולם לא לזרוק החוצה.
- לסמן `Partial` כאשר חלק מהמידע נאסף (למשל חוסר הרשאות admin).
- למדוד זמן ריצה (`DurationMs`).
- קריאות בלבד; אין לשנות דבר במחשב הלקוח.

מקורות מידע עיקריים: WMI/CIM (`System.Management`), Windows Registry, Event Log,
ו-APIs של מערכת ההפעלה.

## רשימת ה-Collectors

### 1. SystemInfoCollector (`system`)
- שם מחשב, שם משתמש, דומיין / Workgroup.
- זמן איסוף (UTC), Uptime.
- מקור: `Environment`, WMI `Win32_ComputerSystem`, `Win32_OperatingSystem` (LastBootUpTime).

### 2. OperatingSystemCollector (`os`)
- גרסת Windows, Build, Edition, ארכיטקטורה (32/64), שפה/Locale.
- מקור: WMI `Win32_OperatingSystem`, Registry `HKLM\...\CurrentVersion`.

### 3. HardwareCollector (`hardware`)
- יצרן, דגם, Serial Number, BIOS (יצרן/גרסה/תאריך).
- CPU (שם, ליבות), RAM (סה"כ, מספר מודולים).
- מקור: WMI `Win32_ComputerSystem`, `Win32_BIOS`, `Win32_Processor`, `Win32_PhysicalMemory`.

### 4. DiskCollector (`disks`)
- כל הכוננים הלוגיים: אות, קיבולת, מקום פנוי, אחוז פנוי, סוג (SSD/HDD אם זמין).
- מקור: WMI `Win32_LogicalDisk`, `MSFT_PhysicalDisk`.

### 5. NetworkCollector (`network`)
- כל כרטיסי הרשת הפעילים: שם, MAC, IP פנימי, Gateway, DNS.
- IP חיצוני – אופציונלי (רק אם מוגדר; דורש קריאת רשת יוצאת).
- מקור: `NetworkInterface`, WMI `Win32_NetworkAdapterConfiguration`.

### 6. PrintersCollector (`printers`)
- כל המדפסות: שם, סטטוס, יצרן/דגם, מדפסת ברירת מחדל, יציאה.
- מקור: WMI `Win32_Printer`.

### 7. UsbDevicesCollector (`usbDevices`)
- כל ההתקנים המחוברים: שם, VID/PID, יצרן, סוג (Printer / HID / Camera / USB-Serial וכו').
- מקור: WMI `Win32_PnPEntity`, Registry `HKLM\SYSTEM\CurrentControlSet\Enum\USB`.

### 8. ComPortsCollector (`comPorts`)
- כל פורטי ה-COM: מספר פורט, שם ההתקן המשויך.
- מקור: WMI `Win32_SerialPort` / `Win32_PnPEntity` (class Ports), Registry `HARDWARE\DEVICEMAP\SERIALCOMM`.

### 9. PaymentTerminalsCollector (`paymentTerminals`)
- זיהוי מסופי סליקה לפי USB VID/PID והצלבה מול טבלת יצרנים.
- לכל מסוף: דגם, Vendor, COM Port, USB VID/PID.
- יצרנים לזיהוי: Ingenico, Verifone, PAX, Castles, וכל יצרן נוסף בטבלה.
- מקור: הצלבת פלט `usbDevices` + `comPorts` מול טבלת VID/PID.

מיפוי VID/PID (ראשוני, מורחב לפי הצורך):

```
Ingenico  -> VID 0B00 (וקרוב)
Verifone  -> VID 11CA
PAX       -> VID 0C46 / לפי דגם
Castles   -> VID 1BEC
```

הערה: הטבלה תתוחזק במקום אחד (config) וניתנת לעדכון ללא שינוי קוד.

### 10. WindowsServicesCollector (`services`)
- כל שירותי Yarpa, SQL Server, IIS (אם קיים), ושירותים חשובים נוספים לפי רשימת מעקב.
- לכל שירות: שם, DisplayName, סטטוס (Running/Stopped), StartMode.
- מקור: WMI `Win32_Service`, `ServiceController`.

### 11. SqlServerCollector (`sqlServer`)
- האם מותקן, אילו Instances קיימים, סטטוס שירות לכל Instance, גרסה.
- מקור: Registry `HKLM\SOFTWARE\Microsoft\Microsoft SQL Server`, `Win32_Service` (MSSQL*).

### 12. InstalledSoftwareCollector (`installedSoftware`)
- מוצרי Yarpa, .NET Runtime, SQL, ורכיבים רלוונטיים.
- לכל תוכנה: שם, גרסה, יצרן, תאריך התקנה.
- מקור: Registry `Uninstall` (`HKLM` + `HKCU`, 32/64 bit).

### 13. EventLogCollector (`eventLogs`)
- שגיאות מערכת אחרונות (System / Application), חלון זמן מוגבל (למשל 7 ימים אחרונים).
- לכל אירוע: מקור, EventId, רמה, זמן, הודעה מקוצרת.
- מקור: `EventLog` / `EventLogReader`.

### 14. YarpaVersionCollector (`yarpaVersion`)
- זיהוי אוטומטי של גרסת תוכנת Yarpa המותקנת.
- אסטרטגיית זיהוי (לפי סדר עדיפות, ייקבע סופית מול צוות Yarpa):
  1. Registry ייעודי של Yarpa (אם קיים).
  2. גרסת ה-executable/DLL הראשי (`FileVersionInfo`).
  3. קובץ גרסה/config בתיקיית ההתקנה.
- מקור: Registry + File System.

## סיכום מקורות מידע

- **WMI/CIM**: system, os, hardware, disks, network, printers, usbDevices, comPorts, services, sqlServer.
- **Registry**: os, usbDevices, comPorts, sqlServer, installedSoftware, yarpaVersion.
- **Event Log**: eventLogs.
- **File System**: yarpaVersion.

## הרחבה עתידית

הוספת Collector חדש: יצירת מחלקה שמממשת `ICollector`, רישומה ב-DI, והוספת ה-DTO
המתאים ל-`Yarpa.Contracts`. אין צורך בשינוי ב-Orchestrator או בשרת (השרת שומר את
כל ה-sections גם כ-JSON גולמי).
