Large Folder Finder
====================
Ein Tool zur schnellen Analyse und Auflistung von Ordnerhierarchien.
Nützlich für die Ordneranalyse mit Größenbedingungen und Filtern (Platzhalter, reguläre Ausdrücke).
Hilft bei der Identifizierung von Problemursachen in großen Daten, die von mehreren Benutzern genutzt werden, wie z.B. NAS.


■ Verwendung
--------------------
  1. Wählen Sie den zu untersuchenden Ordner aus.
  2. Drücken Sie die Schaltfläche "▶" (Scannen), um die Suche zu starten.
  3. Die Ergebnisse werden in einem Format ähnlich dem Windows Explorer angezeigt.
  4. Geben Sie Anzeigebedingungen an: Mindestgröße zum Extrahieren, Filter, Sortierung, Ordner einklappen.
  5. Drücken Sie die Kopierschaltfläche oben rechts, um die Anzeigeergebnisse in die Zwischenablage zu kopieren.
  6. Drücken Sie die Schaltfläche "+" rechts neben der Registerkarte, um einen neuen Scan zu starten und dabei den Verlauf beizubehalten.
    Der Verlauf bleibt auch nach dem Schließen der Anwendung erhalten.
※ Durch Ausführen der App mit Administratorrechten können Sie auch Ordner mit Administratorrechten auf dem C-Laufwerk analysieren.
※ Sie können die Sprache wechseln oder das Layout über Menü/Ansicht ändern.
※ Sie können Einstellungen über Menü/Ansicht/Erweiterte Einstellungen öffnen(S) ändern. Details siehe unten.


■ Über Anzeigefunktionen
-------------------
1. Sortieren
  Klicken Sie auf jedes Label (Name, Größe, Änderungsdatum, Typ), um die Anzeigereihenfolge zu sortieren.
  Klicken Sie erneut, um zwischen aufsteigend/absteigend zu wechseln.
2. Dateien auch anzeigen
  Aktivieren Sie dies, um auch Dateien anzuzeigen.
3. Mindestgröße
  Geben Sie die Mindestgröße der anzuzeigenden Ordner oder Dateien an. Elemente, die gleich oder größer als der eingestellte Wert sind, werden angezeigt.
  Geben Sie 0 ein, wenn Sie alles anzeigen möchten.
  Einheiten können von Byte bis TB ausgewählt werden.
4. Filter
Platzhalter: Gleiches Verhalten wie Windows Explorer.
  * Erlaubt das Übereinstimmen mit beliebigen Zeichenfolgen. Beispiel) *.txt Alle txt-Dateien mit beliebigem Namen. Beispiel 2) *Daten* Alle Dateien mit "Daten" im Namen.
  ? Erlaubt das Übereinstimmen mit einem beliebigen einzelnen Zeichen. Beispiel) 202?Jahr → 2020Jahr~2029Jahr usw. (passt auch auf Nicht-Ziffern)
  ~ Vor (* oder ?) platzieren, um nach diesen Zeichen selbst zu suchen. Beispiel) ~?.txt → Sucht nach ?.txt
Regulärer Ausdruck: Erweiterte Filterfunktion (wird von Ingenieuren usw. verwendet)
  Kann Dinge tun, die Platzhalter nicht können. Nur Zahlen, Kleinbuchstaben, Großbuchstaben abgleichen, nur nicht übereinstimmende Elemente extrahieren usw.
  Es ist komplex, suchen Sie daher separat nach "Verwendung regulärer Ausdrücke".
  Es gibt auch Tools zum Überprüfen regulärer Ausdrücke, um zu überprüfen, ob Ihre Suche korrekt funktioniert.
5. Leerzeichen/Tab
  Geben Sie an, ob der Raum zwischen Name und Größe beim Drücken der Kopierschaltfläche mit Leerzeichen oder Tabs gefüllt werden soll.


■ Wenn es nicht richtig funktioniert
------------------------
※ Sie können das Verhalten der App über Menü/Ansicht/Protokolle überprüfen.
※ Wenn sich die App seltsam verhält, kann das Löschen der Daten im folgenden Ordner den Cache zurücksetzen und die Funktionalität wiederherstellen.
    %LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder


■ Über erweiterte Einstellungen (Config.txt)
--------------------
Durch Bearbeiten von "Config.txt" im Ausführungsverzeichnis sind detailliertere Verhaltenseinstellungen möglich.
Klicken Sie auf die Schaltfläche "⚙" in der Benutzeroberfläche, um sie sofort mit einem Texteditor wie Notepad zu öffnen.
Die Konfiguration muss dem YAML-Format folgen. Wenn Sie eigene Kommentare hinzufügen möchten, setzen Sie # davor.

    ▽ Konfigurierbare Elemente: (Standard)
    UseParallelScan: true
        Typ: bool (true/false)
        Beschreibung: Parallele Verarbeitung aktivieren
        Erwarteter Wert (true): Effektiv für NAS (Netzwerkspeicher) usw. Lokale SSDs sind schnell, daher kann der Overhead der Parallelisierung größer sein.

    SkipFolderCount: false
        Typ: bool (true/false)
        Beschreibung: Ob die Vorzählung für die Fortschrittsanzeige übersprungen und sofort mit dem Scannen begonnen werden soll
        Wenn true, kann der Fortschrittsprozentsatz nicht angezeigt werden, da die Gesamtzahl unbekannt ist.

    MaxDepthForCount: 3
        Typ: int (natürliche Zahl)
        Beschreibung: Maximale Hierarchietiefe für die Vorzählung von Ordnern zur Bestimmung des Fortschrittsprozentsatzes
        Größere angegebene Hierarchie kann mehr Zeit in Anspruch nehmen. Stattdessen verbessert sich die Fortschrittsgenauigkeit.
        Erwarteter Wert (3): NAS: 3~6, Interner PC: 7~

    UsePhysicalSize: true
        Typ: bool (true/false)
        Beschreibung: Ob die "zugewiesene Größe auf dem Datenträger" unter Berücksichtigung der Clustergröße berechnet werden soll
        Erwarteter Wert (true): Normalerweise wird empfohlen, true beizubehalten. Die Ergebnisse sind näher an den Windows-Eigenschaftsanzeigen. Wenn false, wird nach Dateigröße berechnet.
        Bevor Sie dies anpassen, empfehlen wir, als Administrator auszuführen. Systemdateien werden in Berechnungen einbezogen, um die Genauigkeit zu erhöhen.

    OldDataThresholdDays: 30
        Typ: int (Nicht-negative ganze Zahl)
        Beschreibung: Markiert den Tab gelb, um alte Scandaten anzuzeigen, wenn die angegebene Anzahl an Tagen vergangen ist.
        Erwarteter Wert: Nach Benutzerpräferenz.

■ So fügen Sie Sprachdateien hinzu
--------------------
Dieses Tool unterstützt mehrere Sprachen, und Sie können neue hinzufügen.
1. Öffnen Sie den Ordner "Languages" in derselben Hierarchie wie die ausführbare Datei der App (.exe).
2. Kopieren Sie eine vorhandene Datei wie "en.yaml" und benennen Sie sie in den Kulturcode der Sprache um, die Sie hinzufügen möchten (z.B. "fr.yaml" für Französisch).
   * Eine Liste der Kulturcodes (z.B.: ja-JP / ja) finden Sie unter:
   https://learn.microsoft.com/de-de/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. Bearbeiten Sie den Text in der YAML-Datei (speichern Sie im UTF-8-Format).
4. Starten Sie die App neu, und die neue Sprache wird im Menü "Language" angezeigt.
※ Erstellen und fügen Sie bei Bedarf Readme_<language_code>.txt hinzu, indem Sie andere Dateien als Referenz verwenden.


■ Vollständige Deinstallation (Einstellungen und Protokolle löschen)
--------------------
Um Einstellungen und Ausführungsprotokolle dieses Tools vollständig zu entfernen, löschen Sie bitte manuell den folgenden Ordner:
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
(Sie können ihn direkt öffnen, indem Sie den obigen Pfad in die Explorer-Adressleiste einfügen)


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
