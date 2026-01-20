Large Folder Finder
====================
Ein Werkzeug, um große Ordner schnell zu finden und aufzulisten.


■ Verwendung
--------------------
1. Wählen Sie den Ordner aus, den Sie untersuchen möchten.
2. Geben Sie die Mindestgröße an, die Sie extrahieren möchten.
3. Drücken Sie die Taste „Scannen“, um die Suche zu starten.
4. Die Ergebnisse werden im Textformat angezeigt.
5. Drücken Sie die Kopiertaste (📄-Symbol) oben rechts, um die Ergebnisse in die Zwischenablage zu kopieren.


■ Erweiterte Einstellungen (Config.txt)
--------------------
Durch Bearbeiten der Datei „Config.txt“ im Anwendungsverzeichnis können Sie detaillierte Verhaltensweisen konfigurieren.
Klicken Sie auf die Schaltfläche „⚙“ in der Benutzeroberfläche, um sie sofort mit einem Texteditor wie Editor zu öffnen.
Die Konfiguration muss dem YAML-Format folgen. Wenn Sie eigene Kommentare hinzufügen möchten, stellen Sie diesen ein # voran.

    ▽ Konfigurierbare Elemente: (Vorgabe)
    UseParallelScan: true
        Typ: bool (true/false)
        Beschreibung: Parallele Verarbeitung aktivieren.
        Kontext (true): Effektiv für NAS (Netzwerkspeicher) usw. Da lokale SSDs schnell sind, kann der Overhead der Parallelisierung größer sein.

    SkipFolderCount: false
        Typ: bool (true/false)
        Beschreibung: Ob der Vorab-Count für die Fortschrittsanzeige übersprungen und der Scan sofort gestartet werden soll.
        Wenn auf true gesetzt, kann kein Fortschrittsprozentsatz angezeigt werden, da die Gesamtzahl der Ordner unbekannt ist.

    MaxDepthForCount: 3
        Typ: int (natürliche Zahl)
        Beschreibung: Maximale Hierarchietiefe für das Vorab-Zählen von Ordnern zur Bestimmung des Fortschrittsprozentsatzes.
        Größere Werte können mehr Zeit in Anspruch nehmen, erhöhen aber die Genauigkeit der Fortschrittsanzeige.
        Beispiel (3): NAS: 3~6, Interner PC: 7~

    UsePhysicalSize: true
        Typ: bool (true/false)
        Beschreibung: Ob die „belegte Größe auf dem Datenträger“ unter Berücksichtigung der Clustergröße berechnet werden soll.
        Beispiel (true): Normalerweise wird true empfohlen. Die Ergebnisse liegen näher an den Windows-Eigenschaftenanzeigen. Wenn false, wird nach der tatsächlichen Dateigröße berechnet.
        Bevor Sie dies anpassen, empfehlen wir, die App als Administrator auszuführen, um Systemdateien genau in die Berechnungen einzubeziehen.


■ Hinzufügen von Sprachdateien
--------------------
Dieses Tool unterstützt mehrere Sprachen, und Sie können neue hinzufügen.
1. Öffnen Sie den Ordner „Languages“ im selben Verzeichnis wie die ausführbare Datei (.exe).
2. Kopieren Sie eine vorhandene Datei wie „en.yaml“ und benennen Sie sie in den Kulturcode der Sprache um, die Sie hinzufügen möchten (z. B. „fr.yaml“ für Französisch).
   * Eine Liste der Kulturcodes finden Sie in der Microsoft-Dokumentation:
   https://learn.microsoft.com/de-de/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. Bearbeiten Sie den Text in der YAML-Datei (im UTF-8-Format speichern).
4. Starten Sie die App neu, und die neue Sprache erscheint im Menü „Language“.
* Erstellen und fügen Sie bei Bedarf eine Readme_<code>.txt hinzu, indem Sie sich an anderen Dateien orientieren.


■ Vollständige Deinstallation (Einstellungen und Protokolle entfernen)
--------------------
Um Einstellungen und Protokolle dieses Tools vollständig zu entfernen, löschen Sie bitte manuell den folgenden Ordner:
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
(Sie können ihn direkt öffnen, indem Sie den obigen Pfad in die Adresszeile des Explorers einfügen)


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
