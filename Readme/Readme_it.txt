Large Folder Finder
====================
Uno strumento per estrarre ed elencare rapidamente le cartelle più grandi di una dimensione specificata.


■ Come usare
--------------------
1. Selezionare la cartella che si desidera esaminare.
2. Specificare la dimensione minima che si desidera esaminare.
3. Premere il pulsante "Scan" per avviare la ricerca.
4. I risultati sono visualizzati in formato testo.
5. Premere il pulsante di copia (icona 📄) in alto a destra per copiare i risultati negli appunti.


■ Impostazioni avanzate (Config.txt)
--------------------
Modificando "Config.txt" nella directory dell'applicazione, è possibile configurare comportamenti dettagliati.
Fare clic sul pulsante "⚙" sull'interfaccia utente per aprirlo immediatamente con un editor di testo come Blocco note.
La configurazione deve seguire il formato YAML. Se si desidera aggiungere i propri commenti, anteporre il carattere #.

    ▽ Voci configurabili: (Predefinito)
    UseParallelScan: true
        Tipo: bool (true/false)
        Descrizione: Abilita la scansione parallela.
        Contesto (true): Efficace per NAS (archiviazione di rete) ecc. Poiché gli SSD locali sono veloci, il sovraccarico della parallelizzazione potrebbe essere maggiore.

    SkipFolderCount: false
        Tipo: bool (true/false)
        Descrizione: Se saltare il pre-conteggio per la visualizzazione dell'avanzamento e avviare immediatamente la scansione.
        Se impostato su true, la percentuale di avanzamento non può essere visualizzata perché il numero totale di cartelle è sconosciuto.

    MaxDepthForCount: 3
        Tipo: int (numero naturale)
        Descrizione: Profondità massima della gerarchia per il pre-conteggio delle cartelle per determinare la percentuale di avanzamento.
        Valori più alti possono richiedere più tempo ma aumentano la precisione dell'avanzamento.
        Esempio (3): NAS: 3~6, PC interno: 7~

    UsePhysicalSize: true
        Tipo: bool (true/false)
        Descrizione: Indica se calcolare la "dimensione allocata su disco" considerando la dimensione del cluster.
        Esempio (true): In genere si consiglia di impostare su true. I risultati saranno più vicini alla visualizzazione delle proprietà di Windows. Se false, il calcolo avviene in base alla dimensione effettiva del file.
        Prima di regolare questa impostazione, si consiglia di eseguire l'app come amministratore per includere con precisione i file di sistema nei calcoli.


■ Come aggiungere file di lingua
--------------------
Questo strumento supporta più lingue ed è possibile aggiungerne di nuove.
1. Aprire la cartella "Languages" nella stessa directory dell'eseguibile (.exe).
2. Copiare un file esistente come "en.yaml" e rinominarlo con il codice di cultura della lingua che si desidera aggiungere (ad esempio, "fr.yaml" per il francese).
   * Consultare la documentazione Microsoft per un elenco di codici di cultura:
   https://learn.microsoft.com/it-it/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. Modificare il testo all'interno del file YAML (salvare in formato UTF-8).
4. Riavviare l'app e la nuova lingua apparirà nel menu "Language".
* Se necessario, creare e aggiungere un Readme_<code>.txt facendo riferimento agli altri file.


■ Disinstallazione pulita (Rimuovi impostazioni e log)
--------------------
Per rimuovere completamente le impostazioni e i log di esecuzione di questo strumento, eliminare manualmente la seguente cartella:
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
(È possibile aprirla direttamente incollando il percorso sopra indicato nella barra degli indirizzi di Esplora file)


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
