Large Folder Finder
====================
Un outil pour analyser et lister rapidement les hiérarchies de dossiers.
Utile pour l'analyse de dossiers en utilisant des conditions de taille et des filtres (caractères génériques, expressions régulières).
Aide à identifier la cause des problèmes dans les grandes données utilisées par plusieurs utilisateurs, comme les NAS.


■ Comment utiliser
--------------------
  1. Sélectionnez le dossier que vous souhaitez examiner.
  2. Appuyez sur le bouton "▶" (Scanner) pour démarrer la recherche.
  3. Les résultats sont affichés dans un format similaire à l'Explorateur Windows.
  4. Spécifiez les conditions d'affichage : taille minimale à extraire, filtre, tri, réduire les dossiers.
  5. Appuyez sur le bouton de copie en haut à droite pour copier les résultats d'affichage dans le presse-papiers.
  6. Appuyez sur le bouton "+" à droite de l'onglet pour démarrer une nouvelle analyse tout en conservant l'historique.
    L'historique est conservé même après la fermeture de l'application.
※ L'exécution de l'application avec des privilèges d'administrateur vous permet d'analyser les dossiers avec privilèges d'administrateur dans le lecteur C.
※ Vous pouvez changer la langue ou modifier la mise en page depuis Menu/Affichage.
※ Vous pouvez modifier les paramètres depuis Menu/Affichage/Ouvrir les paramètres avancés(S). Les détails sont décrits ci-dessous.


■ À propos des fonctions d'affichage
-------------------
1. Trier
  Cliquez sur chaque étiquette (Nom, Taille, Date de modification, Type) pour trier l'ordre d'affichage.
  Cliquez à nouveau pour basculer entre croissant/décroissant.
2. Afficher les fichiers
  Cochez ceci pour afficher également les fichiers.
3. Taille minimale
  Spécifiez la taille minimale des dossiers ou fichiers à afficher. Les éléments égaux ou supérieurs à la valeur définie seront affichés.
  Entrez 0 si vous souhaitez tout afficher.
  Les unités peuvent être sélectionnées de Byte à TB.
4. Filtre
Caractère générique : Même comportement que l'Explorateur Windows.
  * Permet de correspondre à n'importe quelle chaîne. Exemple) *.txt Tous les fichiers txt avec n'importe quel nom. Exemple 2) *données* Tous les fichiers avec "données" dans le nom.
  ? Permet de correspondre à n'importe quel caractère unique. Exemple) 202?année → 2020année~2029année, etc. (correspond aussi aux non-chiffres)
  ~ Placer avant (* ou ?) pour rechercher ces caractères eux-mêmes. Exemple) ~?.txt → Recherche ?.txt
Expression régulière : Fonction de filtre avancée (utilisée par les ingénieurs, etc.)
  Peut faire des choses que les caractères génériques ne peuvent pas. Correspondre uniquement aux chiffres, lettres minuscules, lettres majuscules, extraire uniquement les éléments non correspondants, etc.
  C'est complexe, alors recherchez "comment utiliser les expressions régulières" séparément.
  Il existe également des outils de vérification d'expressions régulières disponibles pour vérifier si votre recherche fonctionne correctement.
5. Espace/Tabulation
  Spécifiez s'il faut remplir l'espace entre le nom et la taille avec des espaces ou des tabulations lors de l'appui sur le bouton de copie.


■ Lorsque cela ne fonctionne pas correctement
------------------------
※ Vous pouvez vérifier le comportement de l'application depuis Menu/Affichage/Journaux.
※ Si l'application se comporte étrangement, la suppression des données dans le dossier suivant peut réinitialiser le cache et restaurer la fonctionnalité.
    %LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder


■ À propos des paramètres avancés (Config.txt)
--------------------
En éditant "Config.txt" dans le répertoire d'exécution, des paramètres de comportement plus détaillés sont possibles.
Cliquez sur le bouton "⚙" dans l'interface utilisateur pour l'ouvrir immédiatement avec un éditeur de texte comme le Bloc-notes.
La configuration doit suivre le format YAML. Si vous souhaitez ajouter vos propres commentaires, préfixez-les avec #.

    ▽ Éléments configurables : (Par défaut)
    UseParallelScan: true
        Type : bool (true/false)
        Description : Activer le traitement parallèle
        Valeur attendue (true) : Efficace pour NAS (stockage réseau), etc. Les SSD locaux sont rapides, donc la surcharge de parallélisation peut être plus importante.

    SkipFolderCount: false
        Type : bool (true/false)
        Description : S'il faut ignorer le pré-comptage pour l'affichage de la progression et démarrer l'analyse immédiatement
        Si true, le pourcentage de progression ne peut pas être affiché car le nombre total est inconnu.

    MaxDepthForCount: 3
        Type : int (nombre naturel)
        Description : Profondeur maximale de hiérarchie pour le pré-comptage des dossiers afin de déterminer le pourcentage de progression
        Une hiérarchie spécifiée plus grande peut prendre plus de temps. En revanche, la précision de la progression s'améliore.
        Valeur attendue (3) : NAS : 3~6, PC interne : 7~

    UsePhysicalSize: true
        Type : bool (true/false)
        Description : S'il faut calculer la "taille allouée sur le disque" en tenant compte de la taille du cluster
        Valeur attendue (true) : Il est généralement recommandé de garder true. Les résultats seront plus proches des affichages de propriétés Windows. Si false, il calcule par taille de fichier.
        Avant d'ajuster cela, nous recommandons d'exécuter en tant qu'administrateur. Les fichiers système seront inclus dans les calculs pour plus de précision.

    OldDataThresholdDays: 30
        Type : int (Entier non négatif)
        Description : Surligne l'onglet en jaune pour indiquer des données d'analyse anciennes si le nombre de jours spécifié est écoulé.
        Valeur attendue : Préférence de l'utilisateur.

■ Comment ajouter des fichiers de langue
--------------------
Cet outil prend en charge plusieurs langues et vous pouvez en ajouter de nouvelles.
1. Ouvrez le dossier "Languages" dans la même hiérarchie que l'exécutable de l'application (.exe).
2. Copiez un fichier existant comme "en.yaml" et renommez-le en code de culture de la langue que vous souhaitez ajouter (par exemple, "fr.yaml" pour le français).
   * Consultez ce qui suit pour une liste de codes de culture (par exemple : ja-JP / ja) :
   https://learn.microsoft.com/fr-fr/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. Modifiez le texte dans le fichier YAML (enregistrez au format UTF-8).
4. Redémarrez l'application et la nouvelle langue apparaîtra dans le menu "Language".
※ Si nécessaire, créez et ajoutez Readme_<language_code>.txt en vous référant à d'autres fichiers.


■ Désinstallation complète (Supprimer les paramètres et les journaux)
--------------------
Pour supprimer complètement les paramètres et les journaux d'exécution de cet outil, veuillez supprimer manuellement le dossier suivant :
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
(Vous pouvez l'ouvrir directement en collant le chemin ci-dessus dans la barre d'adresse de l'Explorateur)


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
