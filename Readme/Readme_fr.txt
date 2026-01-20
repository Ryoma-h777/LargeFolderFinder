Large Folder Finder
====================
Un outil pour extraire et lister rapidement les dossiers plus volumineux qu'une taille spécifiée.


■ Comment utiliser
--------------------
1. Sélectionnez le dossier que vous souhaitez examiner.
2. Spécifiez la taille minimale que vous souhaitez extraire.
3. Appuyez sur le bouton "Scan" pour lancer la recherche.
4. Les résultats s'affichent au format texte.
5. Appuyez sur le bouton de copie (icône 📄) en haut à droite pour copier les résultats dans le presse-papiers.


■ Paramètres avancés (Config.txt)
--------------------
En modifiant "Config.txt" dans le répertoire de l'application, vous pouvez configurer le comportement détaillé.
Cliquez sur le bouton "⚙" de l'interface utilisateur pour l'ouvrir immédiatement avec un éditeur de texte comme le Bloc-notes.
La configuration doit suivre le format YAML. Si vous souhaitez ajouter vos propres commentaires, faites-les précéder d'un #.

    ▽ Éléments configurables : (Par défaut)
    UseParallelScan: true
        Type : bool (true/false)
        Description : Activer l'analyse parallèle.
        Contexte (true) : Efficace pour les NAS (stockage réseau), etc. Comme les SSD locaux sont rapides, la surcharge de la parallélisation peut être plus importante.

    SkipFolderCount: false
        Type : bool (true/false)
        Description : Indique s'il faut sauter le pré-comptage pour l'affichage de la progression et commencer l'analyse immédiatement.
        S'il est réglé sur true, le pourcentage de progression ne peut pas être affiché car le nombre total de dossiers est inconnu.

    MaxDepthForCount: 3
        Type : int (nombre naturel)
        Description : Profondeur de hiérarchie maximale pour le pré-comptage des dossiers afin de déterminer le pourcentage de progression.
        Des valeurs plus élevées peuvent prendre plus de temps mais augmenter la précision de la progression.
        Exemple (3) : NAS : 3~6, PC interne : 7~

    UsePhysicalSize: true
        Type : bool (true/false)
        Description : Indique s'il faut calculer la "taille allouée sur le disque" en tenant compte de la taille du cluster.
        Exemple (true) : Généralement recommandé de laisser sur true. Les résultats seront plus proches des affichages de propriétés Windows. Si false, le calcul se fait sur la taille réelle du fichier.
        Avant d'ajuster ce paramètre, nous vous recommandons d'exécuter l'application en tant qu'administrateur pour inclure avec précision les fichiers système dans les calculs.


■ Comment ajouter des fichiers de langue
--------------------
Cet outil prend en charge plusieurs langues, et vous pouvez en ajouter de nouvelles.
1. Ouvrez le dossier "Languages" dans le même répertoire que l'exécutable (.exe).
2. Copiez un fichier existant comme "en.yaml" et renommez-le avec le code de culture de la langue que vous souhaitez ajouter (par exemple, "fr.yaml" pour le français).
   * Consultez la documentation Microsoft pour obtenir une liste des codes de culture :
   https://learn.microsoft.com/fr-fr/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. Modifiez le texte dans le fichier YAML (enregistrez au format UTF-8).
4. Redémarrez l'application, et la nouvelle langue apparaîtra dans le menu "Language".
* Si nécessaire, créez et ajoutez un Readme_<code>.txt en vous référant aux autres fichiers.


■ Désinstallation propre (Supprimer les paramètres et les journaux)
--------------------
Pour supprimer complètement les paramètres et les journaux d'exécution de cet outil, veuillez supprimer manuellement le dossier suivant :
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
(Vous pouvez l'ouvrir directement en collant le chemin ci-dessus dans la barre d'adresse de l'Explorateur)


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
