Large Folder Finder
====================
Uno strumento per analizzare e elencare rapidamente le gerarchie di cartelle.
Utile per l'analisi delle cartelle utilizzando condizioni di dimensione e filtri (caratteri jolly, espressioni regolari).
Aiuta a identificare la causa dei problemi in dati di grandi dimensioni utilizzati da più utenti, come NAS.


■ Come usare
--------------------
  1. Selezionare la cartella che si desidera esaminare.
  2. Premere il pulsante "▶" (Scansiona) per avviare la ricerca.
  3. I risultati vengono visualizzati in un formato simile a Esplora file di Windows.
  4. Specificare le condizioni di visualizzazione: dimensione minima da estrarre, filtro, ordinamento, comprimere cartelle.
  5. Premere il pulsante di copia in alto a destra per copiare i risultati di visualizzazione negli appunti.
  6. Premere il pulsante "+" a destra della scheda per avviare una nuova scansione mantenendo la cronologia.
    La cronologia viene conservata anche dopo la chiusura dell'applicazione.
※ L'esecuzione dell'app con privilegi di amministratore consente di analizzare le cartelle con privilegi di amministratore all'interno dell'unità C.
※ È possibile cambiare lingua o modificare il layout da Menu/Visualizza.
※ È possibile modificare le impostazioni da Menu/Visualizza/Apri impostazioni avanzate(S). I dettagli sono descritti di seguito.


■ Informazioni sulle funzioni di visualizzazione
-------------------
1. Ordina
  Fare clic su ciascuna etichetta (Nome, Dimensione, Data di modifica, Tipo) per ordinare l'ordine di visualizzazione.
  Fare nuovamente clic per alternare tra crescente/decrescente.
2. Mostra file
  Selezionare questa opzione per visualizzare anche i file.
3. Dimensione minima
  Specificare la dimensione minima delle cartelle o dei file da visualizzare. Verranno visualizzati gli elementi uguali o superiori al valore impostato.
  Immettere 0 se si desidera visualizzare tutto.
  Le unità possono essere selezionate da Byte a TB.
4. Filtro
Carattere jolly: Stesso comportamento di Esplora file di Windows.
  * Consente di corrispondere a qualsiasi stringa. Esempio) *.txt Tutti i file txt con qualsiasi nome. Esempio 2) *dati* Tutti i file con "dati" nel nome.
  ? Consente di corrispondere a qualsiasi singolo carattere. Esempio) 202?anno → 2020anno~2029anno, ecc. (corrisponde anche ai non cifre)
  ~ Posizionare prima di (* o ?) per cercare quei caratteri stessi. Esempio) ~?.txt → Cerca ?.txt
Espressione regolare: Funzione di filtro avanzata (utilizzata da ingegneri, ecc.)
  Può fare cose che i caratteri jolly non possono. Corrispondere solo numeri, lettere minuscole, lettere maiuscole, estrarre solo elementi non corrispondenti, ecc.
  È complesso, quindi cercare "come usare le espressioni regolari" separatamente.
  Sono disponibili anche strumenti di verifica delle espressioni regolari per verificare se la ricerca funziona correttamente.
5. Spazio/Tabulazione
  Specificare se riempire lo spazio tra nome e dimensione con spazi o tabulazioni quando si preme il pulsante di copia.


■ Quando non funziona correttamente
------------------------
※ È possibile verificare il comportamento dell'app da Menu/Visualizza/Log.
※ Se l'app si comporta in modo strano, l'eliminazione dei dati nella seguente cartella può ripristinare la cache e ripristinare la funzionalità.
    %LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder


■ Informazioni sulle impostazioni avanzate (Config.txt)
--------------------
Modificando "Config.txt" nella directory di esecuzione, sono possibili impostazioni di comportamento più dettagliate.
Fare clic sul pulsante "⚙" nell'interfaccia utente per aprirlo immediatamente con un editor di testo come Blocco note.
La configurazione deve seguire il formato YAML. Se si desidera aggiungere i propri commenti, anteporli con #.

    ▽ Elementi configurabili: (Predefinito)
    UseParallelScan: true
        Tipo: bool (true/false)
        Descrizione: Abilita elaborazione parallela
        Valore previsto (true): Efficace per NAS (archiviazione di rete), ecc. Gli SSD locali sono veloci, quindi l'overhead di parallelizzazione può essere maggiore.

    SkipFolderCount: false
        Tipo: bool (true/false)
        Descrizione: Se saltare il pre-conteggio per la visualizzazione dell'avanzamento e avviare immediatamente la scansione
        Se true, la percentuale di avanzamento non può essere visualizzata perché il numero totale è sconosciuto.

    MaxDepthForCount: 3
        Tipo: int (numero naturale)
        Descrizione: Profondità massima della gerarchia per il pre-conteggio delle cartelle per determinare la percentuale di avanzamento
        Una gerarchia specificata maggiore può richiedere più tempo. Invece, la precisione dell'avanzamento migliora.
        Valore previsto (3): NAS: 3~6, PC interno: 7~

    UsePhysicalSize: true
        Tipo: bool (true/false)
        Descrizione: Se calcolare la "dimensione allocata su disco" considerando la dimensione del cluster
        Valore previsto (true): Di solito si consiglia di mantenere true. I risultati saranno più vicini alle visualizzazioni delle proprietà di Windows. Se false, calcola per dimensione del file.
        Prima di regolare questo, si consiglia di eseguire come amministratore. I file di sistema saranno inclusi nei calcoli per maggiore precisione.

    OldDataThresholdDays: 30
        Tipo: int (Intero non negativo)
        Descrizione: Evidenzia la scheda in giallo per indicare dati di scansione vecchi se è trascorso il numero di giorni specificato.
        Valore previsto: Preferenza dell'utente.

■ Come aggiungere file di lingua
--------------------
Questo strumento supporta più lingue e puoi aggiungerne di nuove.
1. Aprire la cartella "Languages" nella stessa gerarchia dell'eseguibile dell'app (.exe).
2. Copiare un file esistente come "en.yaml" e rinominarlo nel codice cultura della lingua che si desidera aggiungere (ad esempio, "fr.yaml" per il francese).
   * Consultare quanto segue per un elenco di codici cultura (ad esempio: ja-JP / ja):
   https://learn.microsoft.com/it-it/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. Modificare il testo all'interno del file YAML (salvare in formato UTF-8).
4. Riavviare l'app e la nuova lingua apparirà nel menu "Language".
※ Se necessario, creare e aggiungere Readme_<language_code>.txt facendo riferimento ad altri file.


■ Disinstallazione completa (Elimina impostazioni e log)
--------------------
Per rimuovere completamente le impostazioni e i log di esecuzione di questo strumento, eliminare manualmente la seguente cartella:
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
(È possibile aprirla direttamente incollando il percorso sopra nella barra degli indirizzi di Esplora file)


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
