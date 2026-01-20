Large Folder Finder
====================
A tool to quickly extract and list folders larger than a specified size.


■ How to use
--------------------
1. Select the folder you want to investigate.
2. Specify the minimum size you want to extract.
3. Press the "Scan" button to start searching.
4. Results are displayed in text format.
5. Press the copy button (📄 icon) at the top right to copy results to the clipboard.


■ Advanced Settings (Config.txt)
--------------------
By editing "Config.txt" in the application directory, you can configure detailed behavior.
Click the "⚙" button on the UI to open it immediately with a text editor like Notepad.
Configuration must follow the YAML format. If you want to add your own comments, prefix them with #.

    ▽ Configurable items: (Default)
    UseParallelScan: true
        Type: bool (true/false)
        Description: Enable parallel scanning.
        Context (true): Effective for NAS (network storage) etc. Since local SSDs are fast, the overhead of parallelization might be larger.

    SkipFolderCount: false
        Type: bool (true/false)
        Description: Whether to skip the pre-count for progress display and start scanning immediately.
        If set to true, progress percentage cannot be displayed because the total number of folders is unknown.

    MaxDepthForCount: 3
        Type: int (natural number)
        Description: Maximum hierarchy depth for pre-counting folders to determine progress percentage.
        Larger values may take more time but increase progress accuracy.
        Example (3): NAS: 3~6, Internal PC: 7~

    UsePhysicalSize: true
        Type: bool (true/false)
        Description: Whether to calculate the "allocated size on disk" considering cluster size.
        Example (true): Usually recommended to keep true. Results will be closer to Windows property displays. If false, it calculates by actual file size.
        Before adjusting this, we recommend running the app as administrator to accurately include system files in calculations.


■ How to add language files
--------------------
This tool supports multiple languages, and you can add new ones.
1. Open the "Languages" folder in the same directory as the executable (.exe).
2. Copy an existing file like "en.yaml" and rename it to the culture code of the language you want to add (e.g., "fr.yaml" for French).
   * Refer to the following Microsoft documentation for culture codes:
   https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. Edit the text within the YAML file (save in UTF-8 format).
4. Restart the app, and the new language will appear in the "Language" menu.
* If necessary, create and add a Readme_<code>.txt by referring to other files.


■ Clean Uninstall (Remove Settings and Logs)
--------------------
To completely remove settings and execution logs of this tool, please manually delete the following folder:
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
(You can open it directly by pasting the above path into the Explorer address bar)


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
